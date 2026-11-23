using Glazor.Misc;
using MessagePack;

namespace Glazor.Tests;

/// <summary>
/// The contractless resolver has no cycle detection, so a row with a back-reference would overflow the stack while a
/// chunk is written and end the process. <see cref="MessagePackConf.Options"/> refuses a graph deeper than
/// <see cref="MessagePackConf.MaxGraphDepth"/> instead, so the fetch that spills it fails like any other.
/// </summary>
public class SpillDepthGuardTests
{
    public class Customer
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<Order> Orders { get; set; } = [];
    }

    public class Order
    {
        public int Id { get; set; }
        public Customer? Customer { get; set; }
        public Address? ShipTo { get; set; }
    }

    public class Address
    {
        public string? City { get; set; }
    }

    // What EF relationship fix-up builds for an Include of a collection whose elements navigate back
    private static Order CyclicOrder()
    {
        Customer customer = new() { Id = 1, Name = "x" };
        Order order = new() { Id = 2, Customer = customer };
        customer.Orders.Add(order);
        return order;
    }

    [Test]
    public void SerializingACycleThrowsInsteadOfOverflowingTest()
    {
        var ex = Assert.Throws<MessagePackSerializationException>(
            () => MessagePackSerializer.Serialize(new List<Order> { CyclicOrder() }, MessagePackConf.Options));
        Assert.That(ex!.ToString(), Does.Contain("deeper than"));
    }

    [Test]
    public void NestedRowsWithoutCyclesStillRoundTripTest()
    {
        Order order = new() { Id = 2, Customer = new Customer { Id = 1, Name = "x" }, ShipTo = new Address { City = "Rome" } };
        byte[] bin = MessagePackSerializer.Serialize(new List<Order> { order }, MessagePackConf.Options);
        List<Order> back = MessagePackSerializer.Deserialize<List<Order>>(bin, MessagePackConf.Options);
        Assert.That(back[0].Customer!.Name, Is.EqualTo("x"));
        Assert.That(back[0].ShipTo!.City, Is.EqualTo("Rome"));
    }

    /// <summary>
    /// The guard counts on the thread: a refused graph must leave the count where it found it, or the next row on the
    /// same thread would be refused too.
    /// </summary>
    [Test]
    public void ARefusedGraphLeavesTheNextOneAloneTest()
    {
        Assert.Throws<MessagePackSerializationException>(
            () => MessagePackSerializer.Serialize(CyclicOrder(), MessagePackConf.Options));
        Order order = new() { Id = 3, ShipTo = new Address { City = "Milan" } };
        byte[] bin = MessagePackSerializer.Serialize(order, MessagePackConf.Options);
        Assert.That(MessagePackSerializer.Deserialize<Order>(bin, MessagePackConf.Options).ShipTo!.City, Is.EqualTo("Milan"));
    }

    /// <summary>
    /// Through the list: the exception comes out of the Add that evicts the first chunk, where the model's fetch sees it.
    /// </summary>
    [Test]
    public void SpillingACyclicRowSurfacesTheExceptionFromAddTest()
    {
        const int chunksInMemory = OnDiskChunkedList.MinChunksInMemory;
        const int chunkSize = OnDiskChunkedList.MinChunkSize;
        const int count = chunkSize * (chunksInMemory + 3);
        using OnDiskChunkedList<Order> list = new(chunksInMemory, chunkSize);
        var ex = Assert.Throws<MessagePackSerializationException>(() =>
        {
            for (int i = 0; i < count; i++)
                list.Add(CyclicOrder());
        });
        Assert.That(ex!.ToString(), Does.Contain("deeper than"));
    }
}

using Glazor.Grds;
using Glazor.Grds.Mdls;
using Glazor.Grds.Props;
using System.Linq.Expressions;

namespace Glazor.Tests;

public class GettersTests
{
    private sealed class Owner
    {
        public string Name { get; set; } = "";
    }

    private sealed class Row
    {
        public Owner? Owner { get; set; }
    }

    /// <summary>
    /// A column built from an expression reads a null along its path as null in memory, where the expression would throw, and
    /// keeps the expression for the database.
    /// </summary>
    [Test]
    public void AnExpressionColumnReadsANullPathAsNullTest()
    {
        Expression<Func<Row, string>> ownerName = r => r.Owner!.Name;
        var info = ColumnInfo<Row>.Of("Owner", new Column<Row, string>("Owner"), ownerName);
        Assert.Multiple(() =>
        {
            Assert.That(info.Property.GetterFunc(new Row { Owner = new() { Name = "a" } }), Is.EqualTo("a"));
            Assert.That(info.Property.GetterFunc(new Row()), Is.Null, "no NullReferenceException");
            Assert.That(info.Property, Is.InstanceOf<IExprGetter<Row, object>>());
            Assert.That(info.QueryPushDownEnabled, Is.True);
        });
    }
}

namespace Glazor.Tests.Models;

public abstract class Entity
{
    protected internal virtual void OnBeforeInsert()
    {
    }

    protected internal virtual void OnBeforeUpdate()
    {
    }

    protected internal virtual void OnBeforeDelete()
    {
    }
}

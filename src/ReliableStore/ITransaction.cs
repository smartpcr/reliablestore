using System;
using System.Collections.Generic;

namespace ReliableStore
{
    public interface ITransaction : IDisposable
    {
        void Commit();
        void Rollback();
        void Enlist(Action operation, Action rollbackAction);
    }
}

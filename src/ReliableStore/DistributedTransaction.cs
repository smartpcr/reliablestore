using System.Collections.Generic;

namespace ReliableStore
{
    public class DistributedTransaction : ITransaction
    {
        private readonly List<ITransaction> _transactions = new List<ITransaction>();

        public void Add(ITransaction tx)
        {
            _transactions.Add(tx);
        }

        public void Enlist(System.Action operation, System.Action rollbackAction)
        {
            // single operation transaction
            var tx = new TransactionScope();
            tx.Enlist(operation, rollbackAction);
            _transactions.Add(tx);
        }

        public void Commit()
        {
            foreach (var tx in _transactions)
            {
                tx.Commit();
            }
        }

        public void Rollback()
        {
            foreach (var tx in _transactions)
            {
                tx.Rollback();
            }
        }

        public void Dispose()
        {
            foreach (var tx in _transactions)
            {
                tx.Dispose();
            }
        }
    }
}

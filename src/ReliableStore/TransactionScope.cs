using System;
using System.Collections.Generic;

namespace ReliableStore
{
    public class TransactionScope : ITransaction
    {
        private readonly List<Tuple<Action, Action>> _operations = new List<Tuple<Action, Action>>();
        private bool _completed;

        public void Enlist(Action operation, Action rollbackAction)
        {
            if (_completed)
                throw new InvalidOperationException("Transaction already completed");
            _operations.Add(Tuple.Create(operation, rollbackAction));
        }

        public void Commit()
        {
            if (_completed) return;
            foreach (var op in _operations)
            {
                op.Item1();
            }
            _completed = true;
        }

        public void Rollback()
        {
            if (_completed) return;
            for (int i = _operations.Count - 1; i >= 0; i--)
            {
                _operations[i].Item2();
            }
            _completed = true;
        }

        public void Dispose()
        {
            if (!_completed)
            {
                Rollback();
            }
        }
    }
}

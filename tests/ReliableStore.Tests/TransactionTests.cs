using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReliableStore;

namespace ReliableStore.Tests
{
    [TestClass]
    public class TransactionTests
    {
        [TestMethod]
        public void Commit_ShouldApplyOperations()
        {
            var repo = new FileRepository<string>("test.txt");
            using (var tx = new TransactionScope())
            {
                repo.Add("hello", tx);
                tx.Commit();
            }
            Assert.IsTrue(System.IO.File.ReadAllText("test.txt").Contains("hello"));
        }

        [TestMethod]
        public void Rollback_ShouldUndoOperations()
        {
            var repo = new FileRepository<string>("test2.txt");
            using (var tx = new TransactionScope())
            {
                repo.Add("hello", tx);
                tx.Rollback();
            }
            Assert.IsFalse(System.IO.File.ReadAllText("test2.txt").Contains("hello"));
        }
    }
}

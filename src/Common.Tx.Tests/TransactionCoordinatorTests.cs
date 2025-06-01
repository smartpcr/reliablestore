// ------------------------------------------------------------------------------
// <copyright file="TransactionCoordinatorTests.cs" company="Your Company">
//     Copyright (c) Your Company. All rights reserved.
// </copyright>
// ------------------------------------------------------------------------------

namespace Common.Tx.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Tx;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    /// <summary>
    /// Contains unit tests for the <see cref="TransactionCoordinator"/> class.
    /// </summary>
    public sealed class TransactionCoordinatorTests
    {
        private static TransactionCoordinator CreateCoordinator(TransactionOptions options = null)
        {
            var logger = new Mock<ILogger<TransactionCoordinator>>().Object;
            return new TransactionCoordinator(logger, options);
        }

        [Fact]
        public void TransactionCoordinator_InitialState_IsActive()
        {
            var tx = CreateCoordinator();
            Assert.Equal(TransactionState.Active, tx.State);
        }

        [Fact]
        public async Task TransactionCoordinator_RollbackAsync_ChangesStateToRolledBack()
        {
            var tx = CreateCoordinator();
            await tx.RollbackAsync();
            Assert.Equal(TransactionState.RolledBack, tx.State);
        }

        [Fact]
        public async Task TransactionCoordinator_CommitAsync_ChangesStateToCommitted()
        {
            var tx = CreateCoordinator();
            await tx.CommitAsync();
            Assert.Equal(TransactionState.Committed, tx.State);
        }

        [Fact]
        public async Task TransactionCoordinator_RollbackAsync_AfterCommit_DoesNotChangeState()
        {
            var tx = CreateCoordinator();
            await tx.CommitAsync();
            await tx.RollbackAsync();
            Assert.Equal(TransactionState.Committed, tx.State);
        }

        [Fact]
        public async Task TransactionCoordinator_CommitAsync_AfterRollback_ThrowsInvalidOperationException()
        {
            var tx = CreateCoordinator();
            await tx.RollbackAsync();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync());
            Assert.Contains("Cannot commit transaction in state RolledBack", exception.Message);
            Assert.Equal(TransactionState.RolledBack, tx.State);
        }
    }
}


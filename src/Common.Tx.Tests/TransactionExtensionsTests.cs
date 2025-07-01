//-------------------------------------------------------------------------------
// <copyright file="TransactionExtensionsTests.cs" company="CRP">
//     Copyright (c) CRP. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using AwesomeAssertions;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    public class TransactionExtensionsTests : IDisposable
    {
        private readonly Mock<ITransactionFactory> mockTransactionFactory;
        private readonly Mock<ITransaction> mockTransaction;
        private readonly Mock<ITransactionalRepositoryFactory> mockTransactionalRepositoryFactory;

        public TransactionExtensionsTests()
        {
            this.mockTransaction = new Mock<ITransaction>();
            this.mockTransactionFactory = new Mock<ITransactionFactory>();
            this.mockTransactionFactory.Setup(f => f.CreateTransaction(It.IsAny<TransactionOptions>()))
                                 .Returns(this.mockTransaction.Object);
            this.mockTransactionalRepositoryFactory = new Mock<ITransactionalRepositoryFactory>();
        }

        public void Dispose()
        {
            // Cannot set TransactionContext.Current as it has internal setter
            GC.SuppressFinalize(this);
        }

        #region ExecuteInTransactionAsync Tests

        [Fact]
        public async Task ExecuteInTransactionAsync_ActionSucceeds_CommitsTransaction()
        {
            // Arrange
            var actionCompleted = false;
            Func<ITransaction, Task> action = async tx =>
            {
                await Task.Yield();
                actionCompleted = true;
            };

            // Act
            await this.mockTransactionFactory.Object.ExecuteInTransactionAsync(action);

            // Assert
            actionCompleted.Should().BeTrue();
            this.mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            this.mockTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_FuncSucceeds_CommitsTransactionAndReturnsResult()
        {
            // Arrange
            var expectedResult = "success_result";
            Func<ITransaction, Task<string>> func = async tx =>
            {
                await Task.Yield();
                return expectedResult;
            };

            // Act
            var result = await this.mockTransactionFactory.Object.ExecuteInTransactionAsync(func);

            // Assert
            result.Should().Be(expectedResult);
            this.mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            this.mockTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_ActionThrowsException_RollsBackAndRethrows()
        {
            // Arrange
            var originalException = new InvalidOperationException("Action failed");
            Func<ITransaction, Task> action = tx => throw originalException;

            // Act & Assert
            var act = async () => await this.mockTransactionFactory.Object.ExecuteInTransactionAsync(action);
            var ex = await act.Should().ThrowAsync<InvalidOperationException>();
            
            ex.Which.Should().BeSameAs(originalException);
            this.mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
            this.mockTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_ActionThrows_RollbackAlsoThrows_ThrowsAggregateException()
        {
            // Arrange
            var actionException = new InvalidOperationException("Action failed");
            var rollbackException = new Exception("Rollback failed");
            Func<ITransaction, Task> action = tx => throw actionException;
            this.mockTransaction.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(rollbackException);

            // Act & Assert
            var act = async () => await this.mockTransactionFactory.Object.ExecuteInTransactionAsync(action);
            var aggEx = await act.Should().ThrowAsync<AggregateException>();

            aggEx.Which.InnerExceptions.Should().HaveCount(2);
            aggEx.Which.InnerExceptions.Should().Contain(actionException);
            aggEx.Which.InnerExceptions.Should().Contain(rollbackException);
            this.mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
            this.mockTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_SetsTransactionContext()
        {
            // Arrange
            ITransaction? contextDuringAction = null;

            Func<ITransaction, Task> action = async tx =>
            {
                contextDuringAction = TransactionContext.Current;
                await Task.Yield();
            };

            // Act
            await this.mockTransactionFactory.Object.ExecuteInTransactionAsync(action);

            // Assert
            contextDuringAction.Should().NotBeNull();
            contextDuringAction.Should().BeSameAs(this.mockTransaction.Object);
        }

        #endregion

        #region ExecuteWithRetryAsync Tests

        [Fact]
        public async Task ExecuteWithRetryAsync_SuccessOnFirstAttempt()
        {
            // Arrange
            var callCount = 0;
            Func<ITransaction, Task> action = async tx =>
            {
                callCount++;
                await Task.Yield();
            };

            // Act
            await this.mockTransactionFactory.Object.ExecuteWithRetryAsync(action, maxRetries: 3);

            // Assert
            callCount.Should().Be(1);
            this.mockTransactionFactory.Verify(f => f.CreateTransaction(It.IsAny<TransactionOptions>()), Times.Once);
            this.mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            this.mockTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteWithRetryAsync_SuccessAfterOneRetry()
        {
            // Arrange
            var callCount = 0;
            Func<ITransaction, Task> action = async tx =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new TimeoutException("Simulated transient failure");
                }
                await Task.Yield();
            };

            // Act
            await this.mockTransactionFactory.Object.ExecuteWithRetryAsync(action, maxRetries: 3, retryDelay: TimeSpan.FromMilliseconds(1));

            // Assert
            callCount.Should().Be(2);
            this.mockTransactionFactory.Verify(f => f.CreateTransaction(It.IsAny<TransactionOptions>()), Times.Exactly(2));
            this.mockTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            this.mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteWithRetryAsync_NonRetryableException_ThrowsImmediately()
        {
            // Arrange
            var callCount = 0;
            var nonRetryableEx = new ArgumentException("Non-retryable");
            Func<ITransaction, Task> action = tx =>
            {
                callCount++;
                throw nonRetryableEx;
            };

            // Act & Assert
            var act = async () => await this.mockTransactionFactory.Object.ExecuteWithRetryAsync(action, maxRetries: 3, retryDelay: TimeSpan.FromMilliseconds(1));
            var ex = await act.Should().ThrowAsync<ArgumentException>();
            
            callCount.Should().Be(1);
            ex.Which.Should().BeSameAs(nonRetryableEx);
            this.mockTransactionFactory.Verify(f => f.CreateTransaction(It.IsAny<TransactionOptions>()), Times.Once);
            this.mockTransaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
            this.mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteWithRetryAsync_MaxRetriesLessThanOne_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            Func<ITransaction, Task> action = tx => Task.CompletedTask;

            // Act & Assert
            var act1 = async () => await this.mockTransactionFactory.Object.ExecuteWithRetryAsync(action, maxRetries: 0);
            await act1.Should().ThrowAsync<ArgumentOutOfRangeException>();
            
            var act2 = async () => await this.mockTransactionFactory.Object.ExecuteWithRetryAsync(action, maxRetries: -1);
            await act2.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        #endregion

        #region CreateSavepointScopeAsync Tests

        [Fact]
        public async Task CreateSavepointScopeAsync_CreatesSavepointAndReturnsScope()
        {
            // Arrange
            var savepointName = "TestSavepoint";
            var mockSavepoint = new Mock<ISavepoint>().Object;
            this.mockTransaction.Setup(t => t.CreateSavepointAsync(savepointName, It.IsAny<CancellationToken>()))
                           .ReturnsAsync(mockSavepoint);

            // Act
            var scope = await this.mockTransaction.Object.CreateSavepointScopeAsync(savepointName);

            // Assert
            scope.Should().NotBeNull();
            this.mockTransaction.Verify(t => t.CreateSavepointAsync(savepointName, It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion


        public class TestEntity
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
}
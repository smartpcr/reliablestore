//-------------------------------------------------------------------------------
// <copyright file="BasicTransactionTests.cs" company="CRP">
//     Copyright (c) CRP. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx.Tests
{
    using System;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Xunit;

    public class BasicTransactionTests
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ITransactionFactory transactionFactory;

        public BasicTransactionTests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTransactionSupport();
            
            this.serviceProvider = services.BuildServiceProvider();
            this.transactionFactory = this.serviceProvider.GetRequiredService<ITransactionFactory>();
        }

        [Fact]
        public void CreateTransaction_ShouldReturnActiveTransaction()
        {
            // Act
            var transaction = this.transactionFactory.CreateTransaction();

            // Assert
            transaction.Should().NotBeNull();
            transaction.State.Should().Be(TransactionState.Active);
        }

        [Fact]
        public async Task Transaction_CommitEmpty_ShouldSucceed()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();

            // Act
            await transaction.CommitAsync();

            // Assert
            transaction.State.Should().Be(TransactionState.Committed);
        }

        [Fact]
        public async Task Transaction_RollbackEmpty_ShouldSucceed()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();

            // Act
            await transaction.RollbackAsync();

            // Assert
            transaction.State.Should().Be(TransactionState.RolledBack);
        }

        [Fact]
        public async Task Transaction_DoubleCommit_ShouldThrow()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();
            await transaction.CommitAsync();

            // Act & Assert
            var act = async () => await transaction.CommitAsync();
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Transaction_DoubleRollback_ShouldNotThrow()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();
            await transaction.RollbackAsync();

            // Act & Assert
            var act = async () => await transaction.RollbackAsync();
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task TransactionExtensions_ExecuteInTransaction_ShouldCommitAutomatically()
        {
            // Arrange
            var wasInTransaction = false;

            // Act
            await this.transactionFactory.ExecuteInTransactionAsync(
                async (transaction) =>
                {
                    wasInTransaction = TransactionContext.Current != null;
                    await Task.CompletedTask;
                });

            // Assert
            wasInTransaction.Should().BeTrue();
            TransactionContext.Current.Should().BeNull();
        }

        [Fact]
        public async Task TransactionExtensions_WithException_ShouldRollback()
        {
            // Act & Assert
            var act = async () => await this.transactionFactory.ExecuteInTransactionAsync(
                async (transaction) =>
                {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("Test exception");
                });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Test exception");
        }

        [Fact]
        public async Task TransactionExtensions_WithReturnValue_ShouldReturnResult()
        {
            // Act
            var result = await this.transactionFactory.ExecuteInTransactionAsync(
                async (transaction) =>
                {
                    await Task.CompletedTask;
                    return "test result";
                });

            // Assert
            result.Should().Be("test result");
        }

        [Fact]
        public void TransactionFactory_WithCustomOptions_ShouldApplyOptions()
        {
            // Arrange
            var options = new TransactionOptions
            {
                IsolationLevel = IsolationLevel.Serializable,
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Act
            var transaction = this.transactionFactory.CreateTransaction(options);

            // Assert
            transaction.IsolationLevel.Should().Be(IsolationLevel.Serializable);
        }

        [Fact]
        public async Task Transaction_WithTimeout_ShouldTimeout()
        {
            // Arrange
            var options = new TransactionOptions
            {
                Timeout = TimeSpan.FromMilliseconds(100)
            };
            var transaction = this.transactionFactory.CreateTransaction(options);

            // Act
            await Task.Delay(200);

            // Assert
            transaction.State.Should().BeOneOf(TransactionState.Timeout, TransactionState.RolledBack);
        }

        [Fact]
        public async Task Transaction_WithSavepoints_ShouldBeEnabledByDefault()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();

            // Act
            var savepoint = await transaction.CreateSavepointAsync("test");

            // Assert
            savepoint.Should().NotBeNull();
            savepoint.Name.Should().Be("test");
        }

        [Fact]
        public async Task Transaction_WithSavepoints_WhenEnabled_ShouldWork()
        {
            // Arrange
            var options = new TransactionOptions { EnableSavepoints = true };
            var transaction = this.transactionFactory.CreateTransaction(options);

            // Act
            var savepoint = await transaction.CreateSavepointAsync("test");

            // Assert
            savepoint.Should().NotBeNull();
            savepoint.Name.Should().Be("test");
        }
    }
}
//-------------------------------------------------------------------------------
// <copyright file="TransactionResourceTests.cs" company="CRP">
//     Copyright (c) CRP. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Tx.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Xunit;

    public class TransactionResourceTests
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ITransactionFactory transactionFactory;

        public TransactionResourceTests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTransactionSupport();
            
            this.serviceProvider = services.BuildServiceProvider();
            this.transactionFactory = this.serviceProvider.GetRequiredService<ITransactionFactory>();
        }

        [Fact]
        public async Task Transaction_WithResource_ShouldExecuteTwoPhaseCommit()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();
            var resource = new TestResource("resource1");
            
            transaction.EnlistResource(resource);

            // Act
            await transaction.CommitAsync();

            // Assert
            resource.PrepareCalled.Should().BeTrue();
            resource.CommitCalled.Should().BeTrue();
            resource.RollbackCalled.Should().BeFalse();
            transaction.State.Should().Be(TransactionState.Committed);
        }

        [Fact]
        public async Task Transaction_WithMultipleResources_ShouldCommitAll()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();
            var resource1 = new TestResource("resource1");
            var resource2 = new TestResource("resource2");
            var resource3 = new TestResource("resource3");
            
            transaction.EnlistResource(resource1);
            transaction.EnlistResource(resource2);
            transaction.EnlistResource(resource3);

            // Act
            await transaction.CommitAsync();

            // Assert
            foreach (var resource in new[] { resource1, resource2, resource3 })
            {
                resource.PrepareCalled.Should().BeTrue();
                resource.CommitCalled.Should().BeTrue();
                resource.RollbackCalled.Should().BeFalse();
            }
        }

        [Fact]
        public async Task Transaction_WhenResourcePrepareReturnsFalse_ShouldThrowAndRollback()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();
            var resource1 = new TestResource("resource1") { PrepareResult = true };
            var resource2 = new TestResource("resource2") { PrepareResult = false };
            var resource3 = new TestResource("resource3") { PrepareResult = true };
            
            transaction.EnlistResource(resource1);
            transaction.EnlistResource(resource2);
            transaction.EnlistResource(resource3);

            // Act
            var act = async () => await transaction.CommitAsync();
            
            // Assert
            await act.Should().ThrowAsync<TransactionException>()
                .WithMessage("*resource2*failed to prepare*");

            transaction.State.Should().BeOneOf(TransactionState.Failed, TransactionState.RolledBack);
            
            // Resources up to the failed one should have been prepared
            resource1.PrepareCalled.Should().BeTrue();
            resource2.PrepareCalled.Should().BeTrue();
            
            // None should be committed
            resource1.CommitCalled.Should().BeFalse();
            resource2.CommitCalled.Should().BeFalse();
            resource3.CommitCalled.Should().BeFalse();
            
            // All enlisted resources should be rolled back
            resource1.RollbackCalled.Should().BeTrue();
            resource2.RollbackCalled.Should().BeTrue();
            resource3.RollbackCalled.Should().BeTrue();
        }

        [Fact]
        public async Task Transaction_WithSavepoints_ShouldSupportPartialRollback()
        {
            // Arrange
            var transaction = this.transactionFactory.CreateTransaction();
            var resource = new TestResource("resource1");
            
            transaction.EnlistResource(resource);

            // Act
            resource.Operations.Add("op1");
            var savepoint1 = await transaction.CreateSavepointAsync("sp1");
            
            resource.Operations.Add("op2");
            resource.Operations.Add("op3");
            var savepoint2 = await transaction.CreateSavepointAsync("sp2");
            
            resource.Operations.Add("op4");
            resource.Operations.Add("op5");
            
            await transaction.RollbackToSavepointAsync(savepoint2);
            
            // Should still have op1, op2, op3
            resource.Operations.Should().BeEquivalentTo(new[] { "op1", "op2", "op3" });
            
            await transaction.RollbackToSavepointAsync(savepoint1);
            
            // Should only have op1
            resource.Operations.Should().BeEquivalentTo(new[] { "op1" });
            
            await transaction.CommitAsync();

            // Assert
            resource.CommitCalled.Should().BeTrue();
        }

        [Fact]
        public async Task Transaction_WithNestedTransactions_ShouldIsolateContexts()
        {
            // Arrange
            var outerResource = new TestResource("outer");
            var innerResource = new TestResource("inner");
            
            // Act
            await this.transactionFactory.ExecuteInTransactionAsync(async outerTx =>
            {
                outerTx.EnlistResource(outerResource);
                outerResource.Operations.Add("outer-op1");
                
                await this.transactionFactory.ExecuteInTransactionAsync(async innerTx =>
                {
                    innerTx.EnlistResource(innerResource);
                    innerResource.Operations.Add("inner-op1");
                    
                    // Inner and outer transactions should be different
                    innerTx.Should().NotBeSameAs(outerTx);
                    await Task.CompletedTask;
                });
                
                // Inner transaction should be committed
                innerResource.CommitCalled.Should().BeTrue();
                
                outerResource.Operations.Add("outer-op2");
            });

            // Assert
            outerResource.CommitCalled.Should().BeTrue();
            outerResource.Operations.Should().BeEquivalentTo(new[] { "outer-op1", "outer-op2" });
            innerResource.Operations.Should().BeEquivalentTo(new[] { "inner-op1" });
        }

        [Fact]
        public async Task Transaction_WithRetry_ShouldRetryOnTransientFailures()
        {
            // Arrange
            var attempts = 0;
            var successOnAttempt = 3;

            // Act
            await this.transactionFactory.ExecuteWithRetryAsync(
                async tx =>
                {
                    attempts++;
                    if (attempts < successOnAttempt)
                    {
                        throw new TimeoutException("Simulated timeout");
                    }
                    
                    var resource = new TestResource("retry-test");
                    tx.EnlistResource(resource);
                    resource.Operations.Add($"attempt-{attempts}");
                    await Task.CompletedTask;
                },
                maxRetries: 5,
                retryDelay: TimeSpan.FromMilliseconds(10));

            // Assert
            attempts.Should().Be(successOnAttempt);
        }

        private class TestResource : ITransactionalResource
        {
            private readonly Dictionary<string, List<string>> savepoints = new();
            
            public TestResource(string resourceId)
            {
                this.ResourceId = resourceId;
            }

            public string ResourceId { get; }
            public bool PrepareCalled { get; private set; }
            public bool CommitCalled { get; private set; }
            public bool RollbackCalled { get; private set; }
            public bool PrepareResult { get; set; } = true;
            public List<string> Operations { get; } = new();

            public Task<bool> PrepareAsync(ITransaction transaction, CancellationToken cancellationToken = default)
            {
                this.PrepareCalled = true;
                return Task.FromResult(this.PrepareResult);
            }

            public Task CommitAsync(ITransaction transaction, CancellationToken cancellationToken = default)
            {
                this.CommitCalled = true;
                return Task.CompletedTask;
            }

            public Task RollbackAsync(ITransaction transaction, CancellationToken cancellationToken = default)
            {
                this.RollbackCalled = true;
                this.Operations.Clear();
                return Task.CompletedTask;
            }

            public Task CreateSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
            {
                this.savepoints[savepoint.Name] = new List<string>(this.Operations);
                return Task.CompletedTask;
            }

            public Task RollbackToSavepointAsync(ITransaction transaction, ISavepoint savepoint, CancellationToken cancellationToken = default)
            {
                if (this.savepoints.TryGetValue(savepoint.Name, out var savedOps))
                {
                    this.Operations.Clear();
                    this.Operations.AddRange(savedOps);
                }
                return Task.CompletedTask;
            }

            public Task DiscardSavepointDataAsync(ITransaction transaction, ISavepoint savepointToDiscard, CancellationToken cancellationToken = default)
            {
                this.savepoints.Remove(savepointToDiscard.Name);
                return Task.CompletedTask;
            }
        }
    }
}
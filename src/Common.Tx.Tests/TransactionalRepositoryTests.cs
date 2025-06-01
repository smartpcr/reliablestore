// ------------------------------------------------------------------------------
// <copyright file="TransactionalRepositoryTests.cs" company="Your Company">
//     Copyright (c) Your Company. All rights reserved.
// </copyright>
// ------------------------------------------------------------------------------

namespace Common.Tx.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Tx;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    /// <summary>
    /// Unit tests for <see cref="TransactionalRepository{T}"/>.
    /// </summary>
    public sealed class TransactionalRepositoryTests
    {
        public class TestEntity { public string Id { get; set; } public string Value { get; set; } }

        private static TransactionalRepository<TestEntity> CreateRepository(out Mock<IRepository<TestEntity>> repoMock)
        {
            repoMock = new Mock<IRepository<TestEntity>>();
            var logger = new Mock<ILogger<TransactionalRepository<TestEntity>>>();
            return new TransactionalRepository<TestEntity>(repoMock.Object, logger.Object);
        }

        private static ITransaction CreateTransaction(string id = "tx1")
        {
            var tx = new Mock<ITransaction>();
            tx.SetupGet(t => t.TransactionId).Returns(id);
            return tx.Object;
        }

        [Fact]
        public async Task GetAsync_ReturnsPendingOperationValue_WhenExists()
        {
            var repo = CreateRepository(out var repoMock);
            var tx = CreateTransaction();
            var entity = new TestEntity { Id = "1", Value = "A" };
            await repo.SaveAsync(tx, "1", entity);
            var result = await repo.GetAsync(tx, "1");
            Assert.Equal("A", result.Value);
        }

        [Fact]
        public async Task SaveAsync_TracksInsertOrUpdate()
        {
            var repo = CreateRepository(out var repoMock);
            var tx = CreateTransaction();
            repoMock.Setup(r => r.GetAsync("1", It.IsAny<CancellationToken>())).ReturnsAsync((TestEntity)null);
            var entity = new TestEntity { Id = "1", Value = "A" };
            var result = await repo.SaveAsync(tx, "1", entity);
            Assert.Equal(entity, result);
        }

        [Fact]
        public async Task DeleteAsync_TracksDeleteAndReturnsTrue_WhenEntityExists()
        {
            var repo = CreateRepository(out var repoMock);
            var tx = CreateTransaction();
            var entity = new TestEntity { Id = "1", Value = "A" };
            repoMock.Setup(r => r.GetAsync("1", It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            var result = await repo.DeleteAsync(tx, "1");
            Assert.True(result);
        }

        [Fact]
        public async Task DeleteAsync_ReturnsFalse_WhenEntityDoesNotExist()
        {
            var repo = CreateRepository(out var repoMock);
            var tx = CreateTransaction();
            repoMock.Setup(r => r.GetAsync("1", It.IsAny<CancellationToken>())).ReturnsAsync((TestEntity)null);
            var result = await repo.DeleteAsync(tx, "1");
            Assert.False(result);
        }

        [Fact]
        public async Task ExistsAsync_ReturnsTrue_WhenEntityExists()
        {
            var repo = CreateRepository(out var repoMock);
            var tx = CreateTransaction();
            var entity = new TestEntity { Id = "1", Value = "A" };
            repoMock.Setup(r => r.GetAsync("1", It.IsAny<CancellationToken>())).ReturnsAsync(entity);
            var result = await repo.ExistsAsync(tx, "1");
            Assert.True(result);
        }

        [Fact]
        public async Task ExistsAsync_ReturnsFalse_WhenEntityDoesNotExist()
        {
            var repo = CreateRepository(out var repoMock);
            var tx = CreateTransaction();
            repoMock.Setup(r => r.GetAsync("1", It.IsAny<CancellationToken>())).ReturnsAsync((TestEntity)null);
            var result = await repo.ExistsAsync(tx, "1");
            Assert.False(result);
        }

        [Fact]
        public async Task GetAllAsync_AppliesPendingOperations()
        {
            var repo = CreateRepository(out var repoMock);
            var tx = CreateTransaction();
            var entity1 = new TestEntity { Id = "1", Value = "A" };
            var entity2 = new TestEntity { Id = "2", Value = "B" };
            repoMock.Setup(r => r.GetAllAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { entity1, entity2 });
            repoMock.Setup(r => r.GetAsync("2", It.IsAny<CancellationToken>())).ReturnsAsync(entity2);
            await repo.DeleteAsync(tx, "2");
            var all = await repo.GetAllAsync(tx);
            Assert.Single(all);
            Assert.Equal("1", all.First().Id);
        }
    }
}


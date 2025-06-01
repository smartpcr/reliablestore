//-------------------------------------------------------------------------------
// <copyright file="OrderFlowTests.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace IntegrationTests
{
    using System.Net.Http;
    using System.Text;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Newtonsoft.Json;
    [TestClass]
    public class OrderFlowTests
    {
        [TestMethod]
        public void PlaceOrder_ShouldReturnOk()
        {
            using (var client = new HttpClient())
            {
                var order = new { Id = "1", ProductId = "1", Quantity = 1 };
                var content = new StringContent(JsonConvert.SerializeObject(order), Encoding.UTF8, "application/json");
                var response = client.PostAsync("http://localhost:9002/api/process/place-order", content).Result;
                Assert.IsTrue(response.IsSuccessStatusCode);
            }
        }
    }
}

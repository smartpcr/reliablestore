using Microsoft.AspNetCore.Mvc;
using ReliableStore;

namespace OrderService
{
    [ApiController]
    [Route("api/order")]
    public class OrderController : ControllerBase
    {
        private static readonly FileRepository<Order> _repo = new FileRepository<Order>("orders.json");

        [HttpPost("create")]
        public IActionResult Create([FromBody] Order order)
        {
            using (var tx = new TransactionScope())
            {
                _repo.Add(order, tx);
                tx.Commit();
            }
            return Ok();
        }
    }
}

using System.Web.Http;
using ReliableStore;

namespace OrderService
{
    [RoutePrefix("api/order")]
    public class OrderController : ApiController
    {
        private static readonly FileRepository<Order> _repo = new FileRepository<Order>("orders.json");

        [HttpPost, Route("create")]
        public IHttpActionResult Create(Order order)
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

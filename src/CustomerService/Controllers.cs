using System.Web.Http;
using ReliableStore;

namespace CustomerService
{
    [RoutePrefix("api/customer")]
    public class CustomerController : ApiController
    {
        private static readonly FileRepository<Customer> _repo = new FileRepository<Customer>("customers.json");

        [HttpPost, Route("add")]
        public IHttpActionResult Add(Customer customer)
        {
            using (var tx = new TransactionScope())
            {
                _repo.Add(customer, tx);
                tx.Commit();
            }
            return Ok();
        }
    }
}

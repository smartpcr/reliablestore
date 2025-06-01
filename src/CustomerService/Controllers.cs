using Microsoft.AspNetCore.Mvc;
using ReliableStore;

namespace CustomerService
{
    [ApiController]
    [Route("api/customer")]
    public class CustomerController : ControllerBase
    {
        private static readonly FileRepository<Customer> _repo = new FileRepository<Customer>("customers.json");

        [HttpPost("add")]
        public IActionResult Add([FromBody] Customer customer)
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

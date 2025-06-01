using Microsoft.AspNetCore.Mvc;
using ReliableStore;

namespace CatalogService
{
    [ApiController]
    [Route("api/[controller]")]
    public class CatalogController : ControllerBase
    {
        private static readonly FileRepository<Product> _repo = new FileRepository<Product>("catalog.json");

        [HttpPost("add")]
        public IActionResult Add([FromBody] Product product)
        {
            using (var tx = new TransactionScope())
            {
                _repo.Add(product, tx);
                tx.Commit();
            }
            return Ok();
        }
    }
}
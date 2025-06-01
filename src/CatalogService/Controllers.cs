using System.Collections.Generic;
using System.Web.Http;
using ReliableStore;

namespace CatalogService
{
    [RoutePrefix("api/catalog")]
    public class CatalogController : ApiController
    {
        private static readonly FileRepository<Product> _repo = new FileRepository<Product>("catalog.json");

        [HttpPost, Route("add")]
        public IHttpActionResult Add(Product product)
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

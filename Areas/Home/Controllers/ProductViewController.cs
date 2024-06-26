using App.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using XEDAPVIP.Models;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;

namespace App.Areas.Home.Controllers
{
    [Area("Home")]
    public class ProductViewController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ProductViewController> _logger;
        private readonly UserManager<AppUser> _userManager;
        private readonly CacheService _cacheService;
        private readonly CartService _cartService;

        public ProductViewController(AppDbContext context, ILogger<ProductViewController> logger, IMemoryCache cache,
        CacheService cacheService, CartService cartService, UserManager<AppUser> userManager)
        {
            _context = context;
            _logger = logger;
            _cacheService = cacheService;
            _cartService = cartService;
            _userManager = userManager;

        }



        [Route("/product/{slug?}")]
        public async Task<IActionResult> Product(
     string searchString,
     string categoryslug,
     string brandslug,
     [FromQuery(Name = "p")] int currentPage,
     int pagesize,
     string orderby = null,
     [FromQuery(Name = "categories")] List<string> categoryFilters = null,
     [FromQuery(Name = "brands")] List<string> brandFilters = null,
     [FromQuery(Name = "sizes")] List<string> sizeFilters = null,
     [FromQuery(Name = "promotions")] List<string> promotionFilters = null,
     [FromQuery(Name = "prices")] List<string> priceFilters = null
 )
        {
            var categories = await _cacheService.GetCategoriesAsync();
            var brands = await _cacheService.GetBrandsAsync();

            ViewBag.categories = categories;
            ViewBag.categoryslug = categoryslug;
            ViewBag.brands = brands;
            ViewBag.brandslug = brandslug;

            Category category = null;

            var products = _context.Products
                                   .Include(p => p.Brand)
                                   .Include(p => p.Variants)
                                   .Include(p => p.ProductCategories)
                                   .ThenInclude(pc => pc.Category)
                                   .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                products = products.Where(p => p.Name.Contains(searchString));
            }
            if (!string.IsNullOrEmpty(categoryslug))
            {
                category = _context.Categories.Where(c => c.Slug == categoryslug)
                                              .Include(c => c.CategoryChildren)
                                              .FirstOrDefault();

                if (category == null)
                {
                    return NotFound("Không tìm thấy");
                }
            }
            if (promotionFilters != null && promotionFilters.Any())
            {
                if (promotionFilters.Contains("golden-hour"))
                {
                    products = products.Where(p => p.DiscountPrice.HasValue);
                }
                if (promotionFilters.Contains("highest-discount"))
                {
                    products = products.Where(p => p.DiscountPrice.HasValue && (p.Price - p.DiscountPrice.Value) < 50000000);
                }

            }

            if (!string.IsNullOrEmpty(brandslug))
            {
                products = products.Where(p => p.Brand.Slug == brandslug);
            }

            if (category != null)
            {
                products = products.Where(p => p.ProductCategories.Any(pc => pc.CategoryId == category.Id));
            }

            // Apply additional filters
            if (categoryFilters != null && categoryFilters.Any())
            {
                products = products.Where(p => p.ProductCategories.Any(pc => categoryFilters.Contains(pc.Category.Slug)));
            }

            if (brandFilters != null && brandFilters.Any())
            {
                products = products.Where(p => brandFilters.Contains(p.Brand.Slug));
            }

            if (sizeFilters != null && sizeFilters.Any())
            {
                products = products.Where(p => p.Variants.Any(v => sizeFilters.Contains(v.Size)));
            }

            if (priceFilters != null && priceFilters.Any())
            {
                foreach (var priceFilter in priceFilters)
                {
                    switch (priceFilter)
                    {
                        case "under100":
                            products = products.Where(p => p.Price < 1000000);
                            break;
                        case "100to299":
                            products = products.Where(p => p.Price >= 1000000 && p.Price <= 2999999);
                            break;
                        case "300to499":
                            products = products.Where(p => p.Price >= 3000000 && p.Price <= 4999999);
                            break;
                        case "500to699":
                            products = products.Where(p => p.Price >= 5000000 && p.Price <= 6999999);
                            break;
                        case "700to999":
                            products = products.Where(p => p.Price >= 7000000 && p.Price <= 9999999);
                            break;
                        case "above1000":
                            products = products.Where(p => p.Price > 10000000);
                            break;
                    }
                }
            }

            switch (orderby)
            {
                case "date":
                    products = products.OrderByDescending(p => p.DateCreated);
                    break;
                case "priceT":
                    products = products.OrderBy(p => p.Price);
                    break;
                case "priceG":
                    products = products.OrderByDescending(p => p.Price);
                    break;
                default:
                    products = products.OrderByDescending(p => p.DateCreated);
                    break;
            }

            int totalProduc = products.Count();
            if (pagesize <= 0) pagesize = 9;
            int countPages = (int)Math.Ceiling((double)totalProduc / pagesize);
            if (currentPage > countPages)
                currentPage = countPages;
            if (currentPage < 1)
                currentPage = 1;

            var pagingmodel = new PagingModel()
            {
                countpages = countPages,
                currentpage = currentPage,
                generateUrl = (pageNumber) => Url.Action("Product", new
                {
                    categoryslug = categoryslug,
                    brandslug = brandslug,
                    p = pageNumber,
                    pagesize = pagesize
                })
            };

            var productinPage = products.Skip((currentPage - 1) * pagesize)
                                        .Take(pagesize)
                                        .ToList();
            ViewBag.pagingmodel = pagingmodel;
            ViewBag.totalProduc = totalProduc;
            ViewBag.category = category;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_ProductListPartial", productinPage);
            }

            return View(productinPage);
        }


        [Route("/product/{categoryslug}/{productslug}.cshtml")]
        public async Task<IActionResult> DetailProduct(string categoryslug, string brandslug, string productslug)
        {
            var categories = await _cacheService.GetCategoriesAsync();
            var brands = await _cacheService.GetBrandsAsync();

            ViewBag.categories = categories;
            ViewBag.categoryslug = categoryslug;
            ViewBag.brands = brands;
            ViewBag.brandslug = brandslug;

            var product = await _context.Products
                .Where(p => p.Slug == productslug)
                .Include(p => p.Brand)
                .Include(p => p.Variants)
                .Include(p => p.ProductCategories)
                    .ThenInclude(pc => pc.Category)
                .FirstOrDefaultAsync();

            if (product == null)
            {
                return NotFound("Không tìm thấy sản phẩm");
            }

            ViewBag.product = product;
            return View(product);
        }
        [HttpPost]
        [Route("addcart/{productId:int}")]
        public async Task<IActionResult> AddToCart(int productId, [FromQuery] int productCode, [FromBody] CartItem cartItem)
        {
            var productVariant = await _context.productVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(p => p.Id == productCode && p.ProductId == productId);

            if (productVariant == null)
            {
                return NotFound("Product variant not found");
            }

            var user = await GetCurrentUserAsync();
            string userId = user?.Id;  // Get the user ID if authenticated

            List<CartItem> cart;
            if (!string.IsNullOrEmpty(userId))
            {
                cart = _cartService.GetCartItems(userId);
            }
            else
            {
                cart = _cartService.GetCartItems();
                foreach (var c in cart)
                {
                    if (c.VariantId == 0)
                    {
                        c.VariantId = c.Variant.Id;

                    }
                    if (c.Variant == null)
                    {
                        c.Variant = await _context.productVariants.Include(v => v.Product)
                .FirstOrDefaultAsync(p => p.Id == c.VariantId);
                    }
                }
            }

            var existingCartItem = cart.FirstOrDefault(ci => ci.Variant.Id == productCode);

            if (existingCartItem != null)
            {
                // Check if adding the new quantity exceeds the maximum allowed quantity
                if (existingCartItem.Quantity + cartItem.Quantity > productVariant.Quantity)
                {
                    TempData["ErrorMessage"] = "Số lượng thêm vào giỏ hàng hiện có đã vượt quá số lượng hiện có.";
                    return BadRequest(new { message = "Cannot add more of this product. Maximum quantity reached." });
                }
                existingCartItem.Quantity += cartItem.Quantity;
            }
            else
            {
                if (cartItem.Quantity > productVariant.Quantity)
                {
                    TempData["ErrorMessage"] = "Số lượng hiện có trong giỏ hàng đã tối đa.";
                    return BadRequest(new { message = "Cannot add more of this product. Maximum quantity reached." });
                }
                cartItem.Variant = productVariant;
                cartItem.UserId = userId; // Assign userId (may be null for guests)
                cart.Add(cartItem);
            }
            _cartService.SaveCartItems(userId, cart);
            TempData["SuccessMessage"] = "Thêm vào giỏ hàng thành công.";
            return Ok(new { message = "Item added to cart successfully." });
        }



        // Hiện thị giỏ hàng
        [Route("/cart", Name = "cart")]
        public async Task<IActionResult> Cart()
        {
            var categories = await _cacheService.GetCategoriesAsync();
            var brands = await _cacheService.GetBrandsAsync();

            ViewBag.categories = categories;
            ViewBag.brands = brands;
            var user = await GetCurrentUserAsync();
            string userId = user?.Id;  // Get the user ID if authenticated

            List<CartItem> cart;

            if (!string.IsNullOrEmpty(userId))
            {
                cart = _cartService.GetCartItems(userId);
            }
            else
            {
                cart = _cartService.GetCartItems();
                foreach (var c in cart)
                {
                    if (c.VariantId == 0)
                    {
                        c.VariantId = c.Variant.Id;

                    }
                    if (c.Variant == null)
                    {
                        c.Variant = await _context.productVariants.Include(v => v.Product)
                .FirstOrDefaultAsync(p => p.Id == c.VariantId);
                    }
                }
            }

            // Generate the anti-forgery token and pass it to the view
            var tokens = HttpContext.RequestServices.GetService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
            var tokenSet = tokens.GetAndStoreTokens(HttpContext);
            ViewBag.AntiForgeryToken = tokenSet.RequestToken;

            return View(cart);
        }
        [Route("/removecart/{itemId?}")]

        [HttpPost]
        public async Task<IActionResult> DeleteItem(int itemId)
        {
            var user = await GetCurrentUserAsync();
            string userId = user?.Id;  // Get the user ID if authenticated

            if (!string.IsNullOrEmpty(userId))
            {
                var item = await _context.CartItems.FindAsync(itemId);
                if (item != null && item.UserId == userId)
                {
                    _context.CartItems.Remove(item);
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                var session = HttpContext.Session;
                string jsonCart = session.GetString(CartService.CARTKEY);
                if (jsonCart != null)
                {
                    var cartItems = JsonConvert.DeserializeObject<List<CartItem>>(jsonCart);
                    var item = cartItems.FirstOrDefault(ci => ci.Id == itemId);
                    if (item != null)
                    {
                        cartItems.Remove(item);
                        session.SetString(CartService.CARTKEY, JsonConvert.SerializeObject(cartItems));
                    }
                }
            }
            TempData["SuccessMessage"] = "Xoá sản phẩm trong giỏ hàng thành công.";

            return Json(new { success = true });
        }

        [HttpPost]
        [Route("/updatecart")]
        public async Task<IActionResult> UpdateCart([FromBody] List<CartItem> updatedCart)
        {
            var user = await GetCurrentUserAsync();
            string userId = user?.Id;  // Get the user ID if authenticated

            // Update cart items in the cart service
            _cartService.SaveCartItems(userId, updatedCart);
            TempData["SuccessMessage"] = "Cập nhập giỏ hàng thành công.";

            return Ok(new { success = true });
        }





        private Task<AppUser> GetCurrentUserAsync()
        {
            return _userManager.GetUserAsync(HttpContext.User);
        }

    }
}
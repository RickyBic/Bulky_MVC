using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BulkyBookWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class ShoppingCartController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        [BindProperty]
        public ShoppingCartVM ShoppingCartVM { get; set; }

        public ShoppingCartController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public IActionResult Index()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM = new()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId, includeProperties: "Product"),
                OrderHeader = new()
            };

            foreach (var shoppingCart in ShoppingCartVM.ShoppingCartList)
            {
                shoppingCart.Price = GetPriceBasedOnQuantity(shoppingCart);
                ShoppingCartVM.OrderHeader.OrderTotal += shoppingCart.Price * shoppingCart.Count;
            }

            return View(ShoppingCartVM);
        }

        public IActionResult Summary()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM = new()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId, includeProperties: "Product"),
                OrderHeader = new()
            };

            ShoppingCartVM.OrderHeader.ApplicationUser = _unitOfWork.ApplicationUser.Get(u => u.Id == userId);
            ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.ApplicationUser.PhoneNumber;
            ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.ApplicationUser.StreetAddress;
            ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.ApplicationUser.City;
            ShoppingCartVM.OrderHeader.State = ShoppingCartVM.OrderHeader.ApplicationUser.State;
            ShoppingCartVM.OrderHeader.PostalCode = ShoppingCartVM.OrderHeader.ApplicationUser.PostalCode;
            ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.ApplicationUser.Name;

            foreach (var shoppingCart in ShoppingCartVM.ShoppingCartList)
            {
                shoppingCart.Price = GetPriceBasedOnQuantity(shoppingCart);
                ShoppingCartVM.OrderHeader.OrderTotal += shoppingCart.Price * shoppingCart.Count;
            }

            return View(ShoppingCartVM);
        }

        [HttpPost]
        [ActionName("Summary")]
        public IActionResult SummaryPOST()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM.ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId, includeProperties: "Product");
            ShoppingCartVM.OrderHeader.OrderDate = DateTime.Now;
            ShoppingCartVM.OrderHeader.ApplicationUserId = userId;

            ApplicationUser applicationUser = _unitOfWork.ApplicationUser.Get(u => u.Id == userId);

            foreach (var shoppingCart in ShoppingCartVM.ShoppingCartList)
            {
                shoppingCart.Price = GetPriceBasedOnQuantity(shoppingCart);
                ShoppingCartVM.OrderHeader.OrderTotal += shoppingCart.Price * shoppingCart.Count;
            }

            if (applicationUser.CompanyId.GetValueOrDefault() == 0)
            {
                //it's a regular customer user account
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
            }
            else
            {
                //it's a company user account
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayedPayment;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusApproved;
            }

            _unitOfWork.OrderHeader.Add(ShoppingCartVM.OrderHeader);
            _unitOfWork.Save();

            foreach (var shoppingCart in ShoppingCartVM.ShoppingCartList)
            {
                OrderDetail orderDetail = new()
                {
                    ProductId = shoppingCart.ProductId,
                    OrderHeaderId = ShoppingCartVM.OrderHeader.OrderHeaderId,
                    Price = shoppingCart.Price,
                    Count = shoppingCart.Count
                };
                _unitOfWork.OrderDetail.Add(orderDetail);
                _unitOfWork.Save();
            }

            if (applicationUser.CompanyId.GetValueOrDefault() == 0)
            {
                //it's a regular customer user account and we need to capture payment
                //stripe logic
            }

            return RedirectToAction(nameof(OrderConfirmation), new { id = ShoppingCartVM.OrderHeader.OrderHeaderId });
        }

        public IActionResult OrderConfirmation(int id)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.Get(u => u.OrderHeaderId == id, includeProperties: "ApplicationUser");
            orderHeader.OrderStatus = SD.StatusApproved;
            orderHeader.PaymentStatus = SD.PaymentStatusApproved;
            _unitOfWork.OrderHeader.Update(orderHeader);
            _unitOfWork.Save();
            HttpContext.Session.Clear();

            List<ShoppingCart> shoppingCarts = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == orderHeader.ApplicationUserId).ToList();
            _unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);

            _unitOfWork.Save();
            return View(id);
        }

        public IActionResult Plus(int shoppingCartId)
        {
            var shoppingCartFromDb = _unitOfWork.ShoppingCart.Get(u => u.ShoppingCartId == shoppingCartId);
            shoppingCartFromDb.Count += 1;
            _unitOfWork.ShoppingCart.Update(shoppingCartFromDb);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Minus(int shoppingCartId)
        {
            var shoppingCartFromDb = _unitOfWork.ShoppingCart.Get(u => u.ShoppingCartId == shoppingCartId, tracked: true);
            if (shoppingCartFromDb.Count == 1)
            {
                HttpContext.Session.SetInt32(SD.SessionShoppingCart, _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == shoppingCartFromDb.ApplicationUserId).Count() - 1);
                _unitOfWork.ShoppingCart.Remove(shoppingCartFromDb);
            }
            else
            {
                shoppingCartFromDb.Count -= 1;
                _unitOfWork.ShoppingCart.Update(shoppingCartFromDb);
            }
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Remove(int shoppingCartId)
        {
            var shoppingCartFromDb = _unitOfWork.ShoppingCart.Get(u => u.ShoppingCartId == shoppingCartId, tracked: true);
            HttpContext.Session.SetInt32(SD.SessionShoppingCart, _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == shoppingCartFromDb.ApplicationUserId).Count() - 1);
            _unitOfWork.ShoppingCart.Remove(shoppingCartFromDb);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        private double GetPriceBasedOnQuantity(ShoppingCart shopping)
        {
            switch (shopping.Count)
            {
                case <= 50:
                    return shopping.Product.Price;
                case <= 100:
                    return shopping.Product.Price50;
                default:
                    return shopping.Product.Price100;
            }
        }
    }
}

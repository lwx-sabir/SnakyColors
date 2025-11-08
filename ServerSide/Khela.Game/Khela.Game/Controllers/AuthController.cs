using Khela.Game.Database.Models;
using Khela.Game.Dtos.RequestModels;
using Khela.Game.Models.Configs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

namespace Khela.Game.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ITokenService _tokenService;
        private readonly JwtSettings _jwtSettings;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ITokenService tokenService,
            JwtSettings jwtSettings)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _tokenService = tokenService;
            _jwtSettings = jwtSettings;
        }

        // ================= Register =================
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterModel request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var existingEmail = await _userManager.FindByEmailAsync(request.Email);
            if (existingEmail != null)
                return BadRequest(new { message = "Email already exists." });

            var existingUsername = await _userManager.FindByNameAsync(request.Username);
            if (existingUsername != null)
                return BadRequest(new { message = "Username already exists." });

            var user = new ApplicationUser
            {
                UserName = request.Username,
                Email = request.Email,
                CountryCode = "bd"
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(e => e.Description);
                return BadRequest(new { message = "User creation failed.", errors });
            }

            // Optional: assign default role
            // await _userManager.AddToRoleAsync(user, "Player");

            // Generate JWT
            var token = _tokenService.GenerateToken(Guid.Parse(user.Id), user.UserName!);

            var response = new AuthResponse
            {
                Token = token,
                ExpiresIn = _jwtSettings.ExpiryMinutes * 60,
                UserId = user.Id,
                Username = user.UserName!
            };

            return Ok(response);
        }

        // ================= Login =================
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return Unauthorized(new { message = "Invalid credentials." });

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!result.Succeeded)
                return Unauthorized(new { message = "Invalid credentials." });

            var token = _tokenService.GenerateToken(Guid.Parse(user.Id), user.UserName!);

            var response = new AuthResponse
            {
                Token = token,
                ExpiresIn = _jwtSettings.ExpiryMinutes * 60,
                UserId = user.Id,
                Username = user.UserName!
            };

            return Ok(response);
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
            {
                // Do not reveal that user doesn't exist
                return Ok(new { message = "If an account with that email exists, a reset link has been sent." });
            }

            // Generate password reset token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            // TODO: Send this token to user's email. Example:
            var resetUrl = $"{Request.Scheme}://{Request.Host}/reset-password?email={user.Email}&token={Uri.EscapeDataString(token)}";

            // Use your email service here
            // await _emailService.SendPasswordResetEmail(user.Email, resetUrl);

            return Ok(new { message = "Password reset link sent to your email (simulate in logs for now)." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return BadRequest(new { message = "Invalid request." });

            var result = await _userManager.ResetPasswordAsync(user, request.ResetCode, request.NewPassword);

            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(e => e.Description);
                return BadRequest(new { message = "Password reset failed.", errors });
            }

            return Ok(new { message = "Password has been reset successfully." });
        }
    }
}

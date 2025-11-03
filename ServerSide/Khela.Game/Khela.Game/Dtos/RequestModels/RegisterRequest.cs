using System.ComponentModel.DataAnnotations;

namespace Khela.Game.Dtos.RequestModels
{
    public class RegisterModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(3)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public int ExpiresIn { get; set; } // in seconds
        public string UserId { get; set; }
        public string Username { get; set; } = string.Empty;
    }
}

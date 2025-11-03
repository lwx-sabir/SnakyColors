using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Khela.Game.Database.Models
{
    public enum CurrencyType
    {
        Chips,
        Coins,
        Gems,
        Tokens
    }

    [Table("PlayerWallets")]
    public class PlayerWallet
    {
        [Key]
        public Guid WalletId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }  // FK to Users table

        [Required]
        public CurrencyType Currency { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Required]
        public decimal Balance { get; set; } = 0m;

        [Column(TypeName = "decimal(18,2)")]
        public decimal PendingBalance { get; set; } = 0m;  // Optional for in-progress bets

        [Required]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}

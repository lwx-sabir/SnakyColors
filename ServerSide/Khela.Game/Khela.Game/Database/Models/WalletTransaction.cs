using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Khela.Game.Database.Models
{
    public enum TransactionStatus
    {
        Pending,
        Completed,
        Failed,
        Reversed
    }

    public enum TransactionType
    {
        Bet,
        Win,
        Purchase,
        Refund,
        Bonus,
        AdminAdjustment
    }

    [Table("WalletTransactions")]
    public class WalletTransaction
    {
        [Key]
        public Guid TransactionId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid WalletId { get; set; }  // FK to PlayerWallets

        [Column(TypeName = "decimal(18,2)")]
        [Required]
        public decimal Amount { get; set; }

        [Required]
        public TransactionType Type { get; set; }

        [Required]
        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;

        public Guid? GameId { get; set; }  // Optional: which game caused this transaction

        [MaxLength(500)]
        public string Description { get; set; }  // Optional notes, e.g., "Poker win round 5"

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

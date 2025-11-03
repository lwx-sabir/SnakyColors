using CardGames.Platforms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CardGames.Blackjack
{   
    namespace CardGames.Blackjack
    {
        public class Player
        {
            public string Id { get; set; }
            public decimal Balance { get; private set; }

            [JsonInclude]
            public BlackJackHand Hand { get; private set; } = new BlackJackHand();
            
            public decimal Bet { get; private set; }
            public int Wins { get; private set; }
            public int Losses { get; private set; }
            public int Push { get; private set; }
            public string Image { get; private set; } = string.Empty;
            public string Name { get; private set; } = string.Empty;

            [JsonInclude]
            public Deck CurrentDeck { get; set; }

            [JsonInclude]
            public List<Card> Cards => Hand.Cards;

            public Player(string id, decimal balance, string name = "", string image = "")
            {
                Id = id;
                Balance = balance;
                Image = image;
                Name = name;
            }

            // Increase bet for the round
            public void IncreaseBet(decimal amt)
            {
                if (Balance - (Bet + amt) < 0)
                    throw new InvalidOperationException("Not enough balance to increase bet.");
                Bet += amt;
            }

            // Place bet and subtract from balance, returns result
            public PlaceBetResult PlaceBet()
            {
                if (Balance - Bet < 0)
                    throw new InvalidOperationException("Not enough balance to place bet.");

                Balance -= Bet;
                return new PlaceBetResult
                {
                    NewBalance = Balance,
                    PlacedBet = Bet
                };
            }

            // Reset bet
            public void ClearBet() => Bet = 0;

            // Hit action returns the card drawn and hand info
            public HitResult Hit()
            {
                var card = CurrentDeck.Draw();
                Hand.Cards.Add(card);
                int handValue = Hand.GetSumOfHand();

                return new HitResult
                {
                    DrawnCard = card,
                    HandValue = handValue,
                    IsBust = handValue > 21,
                    IsBlackJack = handValue == 21
                };
            }

            // Double down action returns the updated bet, balance, and hit result
            public DoubleDownResult DoubleDown()
            {
                IncreaseBet(Bet);
                Balance -= Bet / 2; // deduct extra half of bet
                var hitResult = Hit();

                return new DoubleDownResult
                {
                    NewBet = Bet,
                    NewBalance = Balance,
                    HitResult = hitResult
                };
            }

            // Create a new hand
            public void NewHand() => Hand = new BlackJackHand();

            // Check hand states
            public bool HasBlackJack() => Hand.GetSumOfHand() == 21;
            public bool HasBust() => Hand.GetSumOfHand() > 21;

            // Record wins/losses/pushes
            public void AddWin(decimal payoutMultiplier = 2)
            {
                Balance += Bet * payoutMultiplier;
                Wins++;
                ClearBet();
            }

            public void AddLoss()
            {
                Losses++;
                ClearBet();
            }

            public void AddPush()
            {
                Push++;
                Balance += Bet; // return bet
                ClearBet();
            }
        }
    }


    public class HitResult
    {
        public Card DrawnCard { get; set; }
        public int HandValue { get; set; }
        public bool IsBust { get; set; }
        public bool IsBlackJack { get; set; }
    }

    public class DoubleDownResult
    {
        public decimal NewBet { get; set; }
        public decimal NewBalance { get; set; }
        public HitResult HitResult { get; set; }
    }

    public class PlaceBetResult
    {
        public decimal NewBalance { get; set; }
        public decimal PlacedBet { get; set; }
    }
}

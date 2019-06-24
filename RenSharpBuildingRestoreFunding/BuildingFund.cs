using RenSharp;
using System;
using System.Collections.Generic;

namespace RenSharpBuildingRestoreFunding
{
    sealed class BuildingFund
    {
        private readonly IDictionary<string, float> funds;

        public BuildingFund(MappedBuildingDefinition mappedDefinition, int buildingId)
        {
            funds = new Dictionary<string, float>();

            MappedDefinition = mappedDefinition ?? throw new ArgumentNullException(nameof(mappedDefinition));
            BuildingId = buildingId;
        }

        public void NotifyPlayerContribution(IcPlayer player)
        {
            funds.TryGetValue(player.PlayerName, out float playerFunds);

            DA.PagePlayer(player, $"Your contribution towards restoring the {DATranslationManager.Translate(BuildingObj)} is {(int)playerFunds} credit(s).");
        }

        public void AddFund(IcPlayer player, float fundAmount)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }
            else if (fundAmount <= 0.0f)
            {
                throw new ArgumentOutOfRangeException($"Argument '{nameof(fundAmount)}' must be > 0");
            }

            string playerName = player.PlayerName;
            if (funds.ContainsKey(playerName))
            {
                funds[playerName] += fundAmount;
            }
            else
            {
                funds.Add(playerName, fundAmount);
            }

            player.Money -= fundAmount;
            player.SetObjectDirtyBit(DirtyBit.BitOccasional, true);
        }

        public float Refund(IcPlayer player)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            string playerName = player.PlayerName;
            if (funds.TryGetValue(playerName, out float fundAmount))
            {
                funds.Remove(playerName);

                player.Money += fundAmount;
                player.SetObjectDirtyBit(DirtyBit.BitOccasional, true);

                DA.PagePlayer(player, $"You have been refunded {(int)fundAmount} credit(s) for the {DATranslationManager.Translate(BuildingObj)}.");

                return fundAmount;
            }
            else
            {
                return 0.0f;
            }
        }

        public void RefundAll()
        {
            foreach (var fundsPair in funds)
            {
                float playerFunds = fundsPair.Value;
                if (playerFunds > 0.0f)
                {
                    IcPlayer player = Engine.FindPlayer(fundsPair.Key);
                    if (player != null)
                    {
                        DA.PagePlayer(player, $"You have been refunded {(int)playerFunds} credit(s) for the {DATranslationManager.Translate(BuildingObj)}.");
                    }
                }
            }

            funds.Clear();
        }

        public bool FundCompleted(float totalFundCost)
        {
            return (TotalFunds + 0.1f >= totalFundCost);
        } 

        public float TotalFunds
        {
            get
            {
                float result = 0.0f;
                foreach (float fundAmount in funds.Values)
                {
                    result += fundAmount;
                }

                return result;
            }
        }

        public MappedBuildingDefinition MappedDefinition
        {
            get;
            private set;
        }

        public int BuildingId
        {
            get;
            private set;
        }

        public IBuildingGameObj BuildingObj
        {
            get
            {
                return Commands.FindObject(BuildingId)?.AsBuildingGameObj();
            }
        }
    }
}

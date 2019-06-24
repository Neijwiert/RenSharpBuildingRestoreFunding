using RenSharp;
using System;
using System.Collections.Generic;

namespace RenSharpBuildingRestoreFunding
{
    public sealed class BuildingRestoreFundingEventClass : RenSharpEventClass
    {
        private static readonly string  SettingsFilename                    = $"{nameof(RenSharpBuildingRestoreFunding)}.ini";
        private static readonly string  GeneralSectionName                  = $"{nameof(RenSharpBuildingRestoreFunding)}";
        private static readonly string  DefsSectionName                     = $"{nameof(RenSharpBuildingRestoreFunding)}Defs";
        private static readonly char    DefsDelim                           = '|';
        
        private static readonly bool    BRFEnabledDefaultValue              = true;
        private static readonly bool    BRFScaleWithPlayerCountDefaultValue = true;
        private static readonly float   BRFScaleDefaultValue                = 1.0f;
        private static readonly int     BRFMaxRestoreCountDefaultValue      = -1;
        private static readonly float   BRFRestoreCostDefaultValue          = 1000.0f;      
        private static readonly bool    BRFAllowRefundDefaultValue          = true;

        private readonly IDictionary<string, MappedBuildingDefinition> mappedDefinitions;
        private readonly IDictionary<int, BuildingFund> buildingFunds;

        public BuildingRestoreFundingEventClass()
        {
            mappedDefinitions = new Dictionary<string, MappedBuildingDefinition>();
            buildingFunds = new Dictionary<int, BuildingFund>();
        }

        public override void UnmanagedAttach()
        {
            DASettingsManager.AddSettings(SettingsFilename);

            RegisterEvent(DAEventType.LevelLoaded);
            RegisterEvent(DAEventType.SettingsLoaded);
            RegisterEvent(DAEventType.PlayerLeave);
            RegisterObjectEvent(DAObjectEventType.Custom, DAObjectEventObjectType.Building);

            RegisterChatCommand(FundChatCommand, "!fund", 1); // !fund <acronym> [<amount>]
            RegisterChatCommand(TotalFundChatCommand, "!totalfund", 1); // !totalfund <acronym>
            RegisterChatCommand(ReFundChatCommand, "!refund"); // !refund [<acronym>]
        }

        public override void LevelLoadedEvent()
        {
            buildingFunds.Clear(); // All deposited funds are cleared

            // The values that may change during maps are reset in the mapped definitions
            foreach (MappedBuildingDefinition mappedDefinition in mappedDefinitions.Values)
            {
                mappedDefinition.Reset();
            }
        }

        public override void SettingsLoadedEvent()
        {
            // Reload the map definitions
            mappedDefinitions.Clear();

            // Load global/per map stuff
            IDASettingsClass settings = DASettingsManager.GetSettings(SettingsFilename);

            BRFEnabled = settings.GetBool(GeneralSectionName, $"{nameof(BRFEnabled)}", BRFEnabledDefaultValue);
            BRFScaleWithPlayerCount = settings.GetBool(GeneralSectionName, $"{nameof(BRFScaleWithPlayerCount)}", BRFScaleWithPlayerCountDefaultValue);
            BRFScale = settings.GetFloat(GeneralSectionName, $"{nameof(BRFScale)}", BRFScaleDefaultValue);
            BRFMaxRestoreCount = settings.GetInt(GeneralSectionName, $"{nameof(BRFMaxRestoreCount)}", BRFMaxRestoreCountDefaultValue);
            BRFRestoreCost = settings.GetFloat(GeneralSectionName, $"{nameof(BRFRestoreCost)}", BRFRestoreCostDefaultValue);
            BRFAllowRefund = settings.GetBool(GeneralSectionName, $"{nameof(BRFAllowRefund)}", BRFAllowRefundDefaultValue);

            // Parse all mappings of preset name = acronym
            IINISection defsSection = settings.GetSection(DefsSectionName);
            if (defsSection != null)
            {
                foreach (IINIEntry currentEntry in defsSection.EntryList)
                {
                    ParseDefinitionEntry(currentEntry);
                }
            }

            // Check if we have per acronym settings
            foreach (var mappedDefinitionPair in mappedDefinitions)
            {
                mappedDefinitionPair.Value.ScaleWithPlayerCount = settings.GetBool(GeneralSectionName, $"{nameof(BRFScaleWithPlayerCount)}_{mappedDefinitionPair.Key}", BRFScaleWithPlayerCount);
                mappedDefinitionPair.Value.Scale = settings.GetFloat(GeneralSectionName, $"{nameof(BRFScale)}_{mappedDefinitionPair.Key}", BRFScale);
                mappedDefinitionPair.Value.MaxRestoreCount = settings.GetInt(GeneralSectionName, $"{nameof(BRFMaxRestoreCount)}_{mappedDefinitionPair.Key}", BRFMaxRestoreCount);
                mappedDefinitionPair.Value.RestoreCost = settings.GetFloat(GeneralSectionName, $"{nameof(BRFRestoreCost)}_{mappedDefinitionPair.Key}", BRFRestoreCost);
            }

            // If this is called during gameplay and some settings changed that allows to restore a building we have to check it now
            TryRestoreAllBuildings();
        }

        public override void PlayerLeaveEvent(IcPlayer player)
        {
            // The cost may have gone down for some buildings, do a check
            TryRestoreAllBuildings(-1);
        }

        public override void CustomEvent(IScriptableGameObj obj, int type, int param, IScriptableGameObj sender)
        {
            // If a building gets restored outside of our control, we have to handle it
            if (type == (int)CustomEventType.CustomEventBuildingRevived)
            {
                if (buildingFunds.TryGetValue(Commands.GetID(obj), out BuildingFund fund))
                {
                    // If refunds are allowed, refund all players
                    if (BRFAllowRefund)
                    {
                        fund.RefundAll(); // TODO?: If a player left should we 'remember' the funds they put in the building and give them back when they rejoin later?
                    }

                    // Remove this building from the funds mapping
                    buildingFunds.Remove(fund.BuildingId);
                }
            }
        }

        private bool FundChatCommand(IcPlayer player, string command, IDATokenClass text, TextMessageEnum chatType, object data)
        {
            if (!BRFEnabled)
            {
                DA.PagePlayer(player, "Building funding is not enabled for this map.");

                return false;
            }

            string acronym = text[1];
            MappedBuildingDefinition mappedDefinition = FindMappedDefinition(acronym);
            ICollection<IDefinitionClass> validatedDefinitions;
            if (mappedDefinition == null || (validatedDefinitions = mappedDefinition.ValidatedDefinitions) == null)
            {
                DA.PagePlayer(player, $"No building found for acronym '{acronym}'.");

                return false;
            }

            IScriptableGameObj destroyedBuilding = FindDestroyedBuilding(validatedDefinitions, player.PlayerType);
            if (destroyedBuilding == null)
            {
                DA.PagePlayer(player, $"No destroyed building found for acronym '{acronym}'.");

                return false;
            }

            if (mappedDefinition.RestoreCountExceeded)
            {
                DA.PagePlayer(player, $"The maximum amount of restores per map for the {DATranslationManager.Translate(destroyedBuilding)} is exceeded.");

                return false;
            }

            // First check if the player specified an amount
            float offeredFundAmount;
            if (text.Size > 1)
            {
                // Make sure to parse as int so that player can't input some weird decimal stuff
                string newFundsStr = text[2];
                if (newFundsStr == null || !int.TryParse(newFundsStr, out int parsedFundAmount) || parsedFundAmount <= 0)
                {
                    DA.PagePlayer(player, $"Invalid fund amount '{newFundsStr}'.");

                    return false;
                }

                offeredFundAmount = parsedFundAmount;
                offeredFundAmount = Math.Min(offeredFundAmount, player.Money);
            }
            else // No amount was given, assume they want to donate everything
            {
                offeredFundAmount = player.Money;
                if (offeredFundAmount <= 0.0f)
                {
                    DA.PagePlayer(player, $"You do not have enough money to fund the '{DATranslationManager.Translate(destroyedBuilding)}'.");

                    return false;
                }
            }

            float totalRestoreCost = mappedDefinition.CalculateTotalRestoreCost(Engine.GetTeamPlayerCount(player.PlayerType));

            // Check if we already have a fund for this building and how much is left until fully funded
            float calculatedFundAmount;
            if (buildingFunds.TryGetValue(destroyedBuilding.ID, out BuildingFund fund))
            {
                float remainingFundAmount = totalRestoreCost - fund.TotalFunds;
                calculatedFundAmount = Math.Min(remainingFundAmount, offeredFundAmount); // Do not exceed the amount required to refund
            }
            else
            {
                calculatedFundAmount = Math.Min(totalRestoreCost, offeredFundAmount); // Do not exceed the amount required to refund

                fund = new BuildingFund(mappedDefinition, destroyedBuilding.ID);
                buildingFunds.Add(destroyedBuilding.ID, fund);
            }

            // Add this player's fund to the total funds
            fund.AddFund(player, calculatedFundAmount);

            DA.TeamColorMessageWithTeamColor(player.PlayerType, $"{player.PlayerName} deposited {(int)(calculatedFundAmount + 0.5f)} credit(s) towards the funding of the {DATranslationManager.Translate(destroyedBuilding)}.");

            // See if we have enough to restore this building
            TryRestoreBuilding(fund);

            return true;
        }

        private bool TotalFundChatCommand(IcPlayer player, string command, IDATokenClass text, TextMessageEnum chatType, object data)
        {
            if (!BRFEnabled)
            {
                DA.PagePlayer(player, "Building funding is not enabled for this map.");

                return false;
            }

            string acronym = text[1];
            MappedBuildingDefinition mappedDefinition = FindMappedDefinition(acronym);
            ICollection<IDefinitionClass> validatedDefinitions;
            if (mappedDefinition == null || (validatedDefinitions = mappedDefinition.ValidatedDefinitions) == null)
            {
                DA.PagePlayer(player, $"No building found for acronym '{acronym}'.");

                return false;
            }

            IScriptableGameObj destroyedBuilding = FindDestroyedBuilding(validatedDefinitions, player.PlayerType);
            if (destroyedBuilding == null)
            {
                DA.PagePlayer(player, $"No destroyed building found for acronym '{acronym}'.");

                return false;
            }

            if (mappedDefinition.RestoreCountExceeded)
            {
                DA.PagePlayer(player, $"The maximum amount of restores per map for the {DATranslationManager.Translate(destroyedBuilding)} is exceeded.");

                return false;
            }

            if (!buildingFunds.TryGetValue(destroyedBuilding.ID, out BuildingFund fund))
            {
                fund = new BuildingFund(mappedDefinition, destroyedBuilding.ID);
                buildingFunds.Add(destroyedBuilding.ID, fund);
            }

            float totalRestoreCost = mappedDefinition.CalculateTotalRestoreCost(Engine.GetTeamPlayerCount(player.PlayerType));

            fund.NotifyPlayerContribution(player);
            DA.TeamColorMessageWithTeamColor(player.PlayerType, $"{DA.MessagePrefix}{(int)(fund.TotalFunds + 0.5f)} out of {(int)(totalRestoreCost + 0.5f)} credit(s) gathered to restore the {DATranslationManager.Translate(destroyedBuilding)}.");

            return true;
        }

        private bool ReFundChatCommand(IcPlayer player, string command, IDATokenClass text, TextMessageEnum chatType, object data)
        {
            // Acronym for a building is specified
            if (text.Size > 0)
            {
                string acronym = text[1];
                MappedBuildingDefinition mappedDefinition = FindMappedDefinition(acronym);
                ICollection<IDefinitionClass> validatedDefinitions;
                if (mappedDefinition == null || (validatedDefinitions = mappedDefinition.ValidatedDefinitions) == null)
                {
                    DA.PagePlayer(player, $"No building found for acronym '{acronym}'.");

                    return false;
                }

                IScriptableGameObj destroyedBuilding = FindDestroyedBuilding(validatedDefinitions, player.PlayerType);
                if (destroyedBuilding == null)
                {
                    DA.PagePlayer(player, $"No destroyed building found for acronym '{acronym}'.");

                    return false;
                }

                if (!buildingFunds.TryGetValue(destroyedBuilding.ID, out BuildingFund fund) || fund.Refund(player) <= 0.0f)
                {
                    DA.PagePlayer(player, $"You haven't funded the {DATranslationManager.Translate(destroyedBuilding)}.");

                    return false;
                }

                DA.TeamColorMessageWithTeamColor(player.PlayerType, $"{DA.MessagePrefix}{(int)(fund.TotalFunds + 0.5f)} out of {(int)(mappedDefinition.CalculateTotalRestoreCost(Engine.GetTeamPlayerCount(player.PlayerType)) + 0.5f)} credit(s) gathered to restore the {DATranslationManager.Translate(destroyedBuilding)}.");

                return true;
            }
            else //  Assume they want all their money back
            {
                int playerCount = Engine.GetTeamPlayerCount(player.PlayerType);

                float totalRefundAmount = 0.0f;
                foreach (BuildingFund fund in buildingFunds.Values)
                {
                    float currentRefundAmount = fund.Refund(player);
                    if (currentRefundAmount > 0.0f)
                    {
                        totalRefundAmount += currentRefundAmount;

                        DA.TeamColorMessageWithTeamColor(player.PlayerType, $"{DA.MessagePrefix}{(int)(fund.TotalFunds + 0.5f)} out of {(int)(fund.MappedDefinition.CalculateTotalRestoreCost(playerCount) + 0.5f)} credit(s) gathered to restore the {DATranslationManager.Translate(fund.BuildingObj)}.");
                    }
                }

                if (totalRefundAmount <= 0.0f)
                {
                    DA.PagePlayer(player, "You haven't funded anything.");

                    return false;
                }

                return true;
            }
        }

        private void ParseDefinitionEntry(IINIEntry entry)
        {
            string entryEntry = entry.Entry;
            string entryValue = entry.Value; 

            if (entryEntry != null && entryValue != null)
            {
                string[] acronyms = entryValue.Split(DefsDelim);
                string[] buildingDefs = entryEntry.Split(DefsDelim);

                IEnumerable<string> validatedBuildingDefs = ValidateBuildingDefinitions(buildingDefs);
                if (validatedBuildingDefs != null)
                {
                    foreach (string acronym in acronyms)
                    {
                        if (acronym != null)
                        {
                            string lowerCaseAcronym = acronym.ToLowerInvariant();
                            if (!mappedDefinitions.ContainsKey(lowerCaseAcronym)) // No duplicate acronyms allowed
                            {
                                mappedDefinitions.Add(lowerCaseAcronym, new MappedBuildingDefinition(validatedBuildingDefs));
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<string> ValidateBuildingDefinitions(string[] buildingDefs)
        {
            ISet<string> result = new HashSet<string>();
            foreach (string buildingDef in buildingDefs)
            {
                // No null or duplicate building preset names allowed
                if (buildingDef != null && !result.Contains(buildingDef))
                {
                    result.Add(buildingDef);
                }
            }

            return (result.Count > 0 ? result : null);
        }

        private MappedBuildingDefinition FindMappedDefinition(string acronym)
        {
            if (acronym == null)
            {
                return null;
            }

            string lowerCaseAcronym = acronym.ToLowerInvariant();
            if (mappedDefinitions.TryGetValue(lowerCaseAcronym, out MappedBuildingDefinition mappedDefinition))
            {
                return mappedDefinition;
            }
            else
            {
                return null;
            } 
        }

        private IScriptableGameObj FindDestroyedBuilding(ICollection<IDefinitionClass> validatedDefinitions, int playerType)
        {
            // Get the first destroyed building that matches the validated definitions
            foreach (IBuildingGameObj building in GameObjManager.BuildingGameObjList)
            {
                if (building.PlayerType == playerType && building.IsDestroyed && validatedDefinitions.Contains(building.Definition))
                {
                    return building;
                }
            }

            return null;
        }

        private void TryRestoreBuilding(BuildingFund fund, int playerCountModifier = 0)
        {
            IBuildingGameObj buildingObj = fund.BuildingObj;
            if (buildingObj == null)
            {
                return;
            }

            int playerCount = Engine.GetTeamPlayerCount(buildingObj.PlayerType) + playerCountModifier;
            if (playerCount > 0)
            {
                // Check if funding is completed
                float totalRestoreCost = fund.MappedDefinition.CalculateTotalRestoreCost(playerCount);
                if (fund.FundCompleted(totalRestoreCost))
                {
                    // Remove first so that we don't assume it was restored outside of our control
                    buildingFunds.Remove(buildingObj.ID);
                    fund.MappedDefinition.CurrentRestoreCount++;

                    // Now do restore
                    Engine.RestoreBuilding(buildingObj);

                    DA.HostMessage($"{Engine.GetTeamName(buildingObj.PlayerType)} has restored their {DATranslationManager.Translate(buildingObj)}.");
                }
                else // Not enough funds gathered yet
                {
                    DA.TeamColorMessageWithTeamColor(buildingObj.PlayerType, $"{DA.MessagePrefix}{(int)(fund.TotalFunds + 0.5f)} out of {(int)(totalRestoreCost + 0.5f)} credit(s) gathered to restore the {DATranslationManager.Translate(buildingObj)}.");
                }
            }
        }

        private void TryRestoreAllBuildings(int playerCountModifier = 0)
        {
            // buildingFunds collection may change when trying to restore it
            IEnumerable<BuildingFund> tmpBuildingFunds = new LinkedList<BuildingFund>(buildingFunds.Values);
            foreach (BuildingFund fund in tmpBuildingFunds)
            {
                TryRestoreBuilding(fund, playerCountModifier);
            }
        }

        public bool BRFEnabled
        {
            get;
            private set;
        }

        public bool BRFScaleWithPlayerCount
        {
            get;
            private set;
        }

        public float BRFScale
        {
            get;
            private set;
        }

        public int BRFMaxRestoreCount
        {
            get;
            private set;
        }

        public float BRFRestoreCost
        {
            get;
            private set;
        }

        public bool BRFAllowRefund
        {
            get;
            private set;
        }
    }
}

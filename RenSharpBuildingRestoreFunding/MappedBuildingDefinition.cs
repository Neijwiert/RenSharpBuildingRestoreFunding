using RenSharp;
using System;
using System.Collections.Generic;

namespace RenSharpBuildingRestoreFunding
{
    sealed class MappedBuildingDefinition
    {
        public MappedBuildingDefinition(IEnumerable<string> definitionNames)
        {
            DefinitionNames = definitionNames;
        }

        public void Reset()
        {
            CurrentRestoreCount = 0;
        }

        public float CalculateTotalRestoreCost(int playerCount)
        {
            if (playerCount <= 0)
            {
                throw new ArgumentOutOfRangeException($"Argument 'playerCount' must be > 0");
            }

            if (ScaleWithPlayerCount)
            {
                return (RestoreCost * playerCount * Scale);
            }
            else
            {
                return RestoreCost;
            }
        }

        public bool RestoreCountExceeded
        {
            get
            {
                return (MaxRestoreCount >= 0 && CurrentRestoreCount >= MaxRestoreCount);
            }
        }

        public IEnumerable<string> DefinitionNames
        {
            get;
            private set;
        }

        public ICollection<IDefinitionClass> ValidatedDefinitions
        {
            get
            {
                ISet<IDefinitionClass> result = new HashSet<IDefinitionClass>(new DefinitionClassEqualityComparer());

                foreach (string definitionName in DefinitionNames)
                {
                    // Check if we have definitions for the preset names in the current map
                    IDefinitionClass foundDefinition = DefinitionMgrClass.FindNamedDefinition(definitionName, false);
                    if (foundDefinition != null)
                    {
                        result.Add(foundDefinition);
                    }
                }

                return (result.Count > 0 ? result : null);
            }
        }

        public bool ScaleWithPlayerCount
        {
            get;
            set;
        }

        public float Scale
        {
            get;
            set;
        }

        public int MaxRestoreCount
        {
            get;
            set;
        }

        public int CurrentRestoreCount
        {
            get;
            set;
        }

        public float RestoreCost
        {
            get;
            set;
        }
    }
}

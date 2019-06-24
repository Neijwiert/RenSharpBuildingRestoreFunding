using RenSharp;
using System.Collections.Generic;

namespace RenSharpBuildingRestoreFunding
{
    sealed class DefinitionClassEqualityComparer : IEqualityComparer<IDefinitionClass>
    {
        public bool Equals(IDefinitionClass x, IDefinitionClass y)
        {
            return x.DefinitionClassPointer.Equals(y.DefinitionClassPointer);
        }

        public int GetHashCode(IDefinitionClass obj)
        {
            return obj.DefinitionClassPointer.GetHashCode();
        }
    }
}

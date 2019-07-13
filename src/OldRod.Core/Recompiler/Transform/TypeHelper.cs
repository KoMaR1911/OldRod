// Project OldRod - A KoiVM devirtualisation utility.
// Copyright (C) 2019 Washi
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using AsmResolver.Net;
using AsmResolver.Net.Cts;
using AsmResolver.Net.Signatures;

namespace OldRod.Core.Recompiler.Transform
{
    public class TypeHelper
    {
        private readonly ITypeDefOrRef _arrayType;
        private readonly ITypeDefOrRef _objectType;

        private readonly IList<TypeSignature> _signedIntegralTypes;
        private readonly IList<TypeSignature> _unsignedIntegralTypes;
        private readonly IList<TypeSignature> _integralTypes;

        public TypeHelper(ReferenceImporter importer)
        {
            _arrayType = importer.ImportType(typeof(Array));
            _objectType = importer.ImportType(typeof(object));

            var typeSystem = importer.TargetImage.TypeSystem;
            
            _signedIntegralTypes = new TypeSignature[]
            {
                typeSystem.SByte,
                typeSystem.Int16,
                typeSystem.Int32,
                typeSystem.IntPtr,
                typeSystem.Int64,
            };
            
            _unsignedIntegralTypes = new TypeSignature[]
            {
                typeSystem.Byte,
                typeSystem.UInt16,
                typeSystem.UInt32,
                typeSystem.UIntPtr,
                typeSystem.UInt64,
            };

            _integralTypes = new TypeSignature[]
            {
                typeSystem.SByte,
                typeSystem.Byte,
                typeSystem.Int16,
                typeSystem.UInt16,
                typeSystem.Int32,
                typeSystem.UInt32,
                typeSystem.IntPtr,
                typeSystem.UIntPtr,
                typeSystem.Int64,
                typeSystem.UInt64,
            };
        }
        
        public IList<ITypeDescriptor> GetTypeHierarchy(ITypeDescriptor type)
        {
            var result = new List<ITypeDescriptor>();
            
            TypeSignature typeSig;
            switch (type)
            {
                // The base type of an array type signature is System.Array, so it needs a special case. 
                // Get the type hierarchy of System.Array and then append the original array type sig.
                case ArrayTypeSignature _:
                case SzArrayTypeSignature _:
                    result.AddRange(GetTypeHierarchy(_arrayType));
                    result.Add(type);
                    return result;
                
                case ByReferenceTypeSignature byRef:
                    result.AddRange(GetTypeHierarchy(byRef.BaseType));
//                    result.Add(byRef);
                    return result;
                
                // Type specification's Resolve method resolves the underlying element type.
                // We therefore need a special case here, to get the type hierarchy of the embedded signature first.
                case TypeSpecification typeSpec:
                    result.AddRange(GetTypeHierarchy(typeSpec.Signature));
                    result.Add(typeSpec);
                    return result;
                
                case GenericParameterSignature genericParam:
                    // TODO: Resolve to actual generic parameter type.
                    result.Add(_objectType);
                    return result;
                
                // No type means no hierarchy.
                case null:
                    return Array.Empty<ITypeDescriptor>();
                
                default:
                    typeSig = type.ToTypeSignature();
                    break;
            }
            
            var genericContext = new GenericContext(null, null);
            
            while (typeSig != null)
            {
                if (typeSig is GenericInstanceTypeSignature genericInstance)
                    genericContext = new GenericContext(genericInstance, null);

                result.Add(typeSig);

                var typeDef = (TypeDefinition) typeSig.ToTypeDefOrRef().Resolve();
                typeSig = typeDef.IsEnum
                    ? typeDef.GetEnumUnderlyingType()
                    : typeDef.BaseType?.ToTypeSignature().InstantiateGenericTypes(genericContext);
            }

            result.Reverse();
            return result;
        }

        private bool IsOnlyIntegral(IEnumerable<ITypeDescriptor> types)
        {
            return types.All(type => _integralTypes.Any(x => type.IsTypeOf(x.Namespace, x.Name)));
        }

        private TypeSignature GetBiggestIntegralType(IEnumerable<ITypeDescriptor> types)
        {
            TypeSignature biggest = null;
            int biggestIndex = 0;
            
            foreach (var type in types)
            {
                int index = 0;
                for (index = 0; index < _integralTypes.Count; index++)
                {
                    if (_integralTypes[index].IsTypeOf(type.Namespace, type.Name))
                        break;
                }

                if (index > biggestIndex && index < _integralTypes.Count)
                {
                    biggest = _integralTypes[index];
                    biggestIndex = index;
                }
            }

            return biggest;
        }
        
        public ITypeDescriptor GetCommonBaseType(ICollection<ITypeDescriptor> types)
        {
            if (types.Count == 1)
                return types.First();
            
            if (IsOnlyIntegral(types))
                return GetBiggestIntegralType(types);
            
            // Obtain all base types for all types.
            var hierarchies = types.Select(GetTypeHierarchy).ToArray();

            if (hierarchies.Length == 0)
                return null;
            
            int shortestSequenceLength = hierarchies.Min(x => x.Count);
            
            // Find the maximum index for which the hierarchies are still the same.
            for (int i = 0; i < shortestSequenceLength; i++)
            {
                // If any of the types at the current position is different, we have found the index.
                if (hierarchies.Any(x => hierarchies[0][i].FullName != x[i].FullName))
                    return i == 0 ? null : hierarchies[0][i - 1];
            }
            
            // We've walked over all hierarchies, just pick the last one of the shortest hierarchy.
            return shortestSequenceLength > 0 
                ? hierarchies[0][shortestSequenceLength - 1] 
                : null;
        }

        public bool IsAssignableTo(ITypeDescriptor from, ITypeDescriptor to)
        {
            if (to == null
                || from.FullName == to.FullName
                || from.IsTypeOf("System", "Int32") && to.IsTypeOf("System", "Boolean"))
            {
                return true;
            }

            if (from.IsValueType != to.IsValueType)
                return false;

            var typeHierarchy = GetTypeHierarchy(from);
            return typeHierarchy.Any(x => x.FullName == to.FullName);
        }
    }
}
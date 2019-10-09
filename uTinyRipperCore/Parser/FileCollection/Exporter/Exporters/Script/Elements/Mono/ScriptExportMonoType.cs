using Mono.Cecil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using uTinyRipper.Assembly;
using uTinyRipper.Assembly.Mono;

namespace uTinyRipper.Exporters.Scripts.Mono
{
	public sealed class ScriptExportMonoType : ScriptExportType
	{
		public ScriptExportMonoType(TypeReference type)
		{
			if (type == null)
			{
				throw new ArgumentNullException(nameof(type));
			}

			Type = type;
			if (type.Module != null)
			{
				Definition = type.Resolve();
			}

			TypeName = GetName(Type);
			NestedName = GetNestedName(Type, TypeName);
			CleanNestedName = ToCleanName(NestedName);
			Module = GetModuleName(Type);
			FullName = GetFullName(Type, Module);
		}

		public static string GetNestedName(TypeReference type)
		{
			string typeName = GetName(type);
			return GetNestedName(type, typeName);
		}

		public static string GetNestedName(TypeReference type, string typeName)
		{
			if (type.IsGenericParameter)
			{
				return typeName;
			}
			if (type.IsArray)
			{
				return GetNestedName(type.GetElementType(), typeName);
			}
			if (type.IsNested)
			{
				string declaringName;
				if (type.IsGenericInstance)
				{
					GenericInstanceType generic = (GenericInstanceType)type;
					int argumentCount = MonoUtils.GetGenericArgumentCount(generic);
					List<TypeReference> genericArguments = new List<TypeReference>(generic.GenericArguments.Count - argumentCount);
					for (int i = 0; i < generic.GenericArguments.Count - argumentCount; i++)
					{
						genericArguments.Add(generic.GenericArguments[i]);
					}
					declaringName = GetNestedGenericName(type.DeclaringType, genericArguments);
				}
				else if(type.HasGenericParameters)
				{
					List<TypeReference> genericArguments = new List<TypeReference>(type.GenericParameters);
					declaringName = GetNestedGenericName(type.DeclaringType, genericArguments);
				}
				else
				{
					declaringName = GetNestedName(type.DeclaringType);
				}
				return $"{declaringName}.{typeName}";
			}
			return typeName;
		}

		public static string ToCleanName(string name)
		{
			int openIndex = name.IndexOf('<');
			if (openIndex == -1)
			{
				return name;
			}
			string firstPart = name.Substring(0, openIndex);
			int closeIndex = name.IndexOf('>');
			string secondPart = name.Substring(closeIndex + 1, name.Length - (closeIndex + 1));
			return firstPart + ToCleanName(secondPart);
		}

		public static string GetName(TypeReference type)
		{
			if (MonoType.IsCPrimitive(type))
			{
				return MonoUtils.ToCPrimitiveString(type.Name);
			}

			if (type.IsGenericInstance)
			{
				GenericInstanceType generic = (GenericInstanceType)type;
				return GetGenericInstanceName(generic);
			}
			else if (type.HasGenericParameters)
			{
				return GetGenericTypeName(type);
			}
			else if (type.IsArray)
			{
				ArrayType array = (ArrayType)type;
				return GetName(array.ElementType) + $"[{new string(',', array.Dimensions.Count - 1)}]";
			}
			return type.Name;
		}

		public static string GetFullName(TypeReference type)
		{
			string module = GetModuleName(type);
			return GetFullName(type, module);
		}

		public static string GetFullName(TypeReference type, string module)
		{
			string name = GetNestedName(type);
			string fullName = $"{type.Namespace}.{name}";
			return ScriptExportManager.ToFullName(module, fullName);
		}

		public static string GetModuleName(TypeReference type)
		{
			return AssemblyManager.ToAssemblyName(type.Scope.Name);
		}

		public static bool HasMember(TypeReference type, string name)
		{
			if (type == null)
			{
				return false;
			}
			if (type.Module == null)
			{
				return false;
			}

			TypeDefinition definition = type.Resolve();
			foreach (FieldDefinition field in definition.Fields)
			{
				if (field.Name == name)
				{
					return true;
				}
			}
			foreach (PropertyDefinition property in definition.Properties)
			{
				if (property.Name == name)
				{
					return true;
				}
			}
			return HasMember(definition.BaseType, name);
		}

		private static string GetNestedGenericName(TypeReference type, List<TypeReference> genericArguments)
		{
			string name = type.Name;
			if (type.HasGenericParameters)
			{
				name = GetGenericTypeName(type, genericArguments);
				int argumentCount = MonoUtils.GetGenericParameterCount(type);
				genericArguments.RemoveRange(genericArguments.Count - argumentCount, argumentCount);
			}
			if (type.IsNested)
			{
				string declaringName = GetNestedGenericName(type.DeclaringType, genericArguments);
				return $"{declaringName}.{name}";
			}
			else
			{
				return name;
			}
		}

		private static string GetGenericTypeName(TypeReference genericType)
		{
			// TypeReference contain parameters with "<!0,!1> (!index)" name but TypeDefinition's name is "<T1,T2> (RealParameterName)"
			genericType = genericType.ResolveOrDefault();
			return GetGenericName(genericType, genericType.GenericParameters);
		}

		private static string GetGenericTypeName(TypeReference genericType, IReadOnlyList<TypeReference> genericArguments)
		{
			genericType = genericType.ResolveOrDefault();
			return GetGenericName(genericType, genericArguments);
		}

		private static string GetGenericInstanceName(GenericInstanceType genericInstance)
		{
			return GetGenericName(genericInstance.ElementType, genericInstance.GenericArguments);
		}

		private static string GetGenericName(TypeReference genericType, IReadOnlyList<TypeReference> genericArguments)
		{
			string name = genericType.Name;
			int argumentCount = MonoUtils.GetGenericParameterCount(genericType);
			if (argumentCount == 0)
			{
				// nested class/enum (of generic class) is generic instance but it doesn't has '`' symbol in its name
				return name;
			}

			int index = name.IndexOf('`');
			StringBuilder sb = new StringBuilder(genericType.Name, 0, index, 50 + index);
			sb.Append('<');
			for (int i = genericArguments.Count - argumentCount; i < genericArguments.Count; i++)
			{
				TypeReference arg = genericArguments[i];
				string argumentName = GetArgumentName(arg);
				sb.Append(argumentName);
				if (i < genericArguments.Count - 1)
				{
					sb.Append(", ");
				}
			}
			sb.Append('>');
			return sb.ToString();
		}

		private static string GetArgumentName(TypeReference type)
		{
			if (MonoType.IsEngineObject(type))
			{
				return $"{type.Namespace}.{type.Name}";
			}

			return GetNestedName(type);
		}

		public override void Init(IScriptExportManager manager)
		{
			base.Init(manager);
			if (Definition != null && Definition.BaseType != null)
			{
				m_base = manager.RetrieveType(Definition.BaseType);
			}

			m_fields = CreateFields(manager);
			CreateMethodsAndProperties(manager, out m_methods, out m_properties);

			if (Type.IsNested)
			{
				m_declaringType = manager.RetrieveType(Type.DeclaringType);
				if (!Type.IsGenericParameter)
				{
					AddAsNestedType();
				}
			}


		}
				
		public override void GetUsedNamespaces(ICollection<string> namespaces)
		{
			if (Definition != null)
			{
				if (Definition.IsSerializable)
				{
					namespaces.Add(ScriptExportAttribute.SystemNamespace);
				}
			}

			base.GetUsedNamespaces(namespaces);
		}

		public override bool HasMember(string name)
		{
			if (base.HasMember(name))
			{
				return true;
			}
			return HasMember(Type, name);
		}

		private IReadOnlyList<ScriptExportField> CreateFields(IScriptExportManager manager)
		{
			if (Definition == null)
			{
				return new ScriptExportField[0];
			}

			List<ScriptExportField> fields = new List<ScriptExportField>();
			foreach (FieldDefinition field in Definition.Fields)
			{
				if (!MonoField.IsSerializableModifier(field))
				{
					continue;
				}

				if (field.FieldType.Module == null)
				{
					// if field has unknown type then consider it as serializable
				}
				else if (field.FieldType.ContainsGenericParameter)
				{
					// if field type has generic parameter then consider it as serializable
				}
				else
				{
					TypeDefinition definition = field.FieldType.Resolve();
					if (definition == null)
					{
						// if field has unknown type then consider it as serializable
					}
					else
					{
						MonoSerializableScope scope = new MonoSerializableScope(field);
						if (!MonoField.IsFieldTypeSerializable(scope))
						{
							continue;
						}
					}
				}

				ScriptExportField efield = manager.RetrieveField(field);
				fields.Add(efield);
			}
			return fields.ToArray();
		}


		private void CreateMethodsAndProperties(IScriptExportManager manager, out IReadOnlyList<ScriptExportMethod> methodList, out IReadOnlyList<ScriptExportProperty> propertyList)
		{
			/*
			 * TODO: Does not find override methods of concrete type when abstract method is generic.
			 * Unclear how to handle, can we make the parent types/methods concrete as we walk the inheritence chain?
			 * Affected game: Tyranny
			 * Related to issue: https://github.com/jbevain/cecil/issues/180
			 * TODO: Abstract classes may have the override methods and properties located in children which may not be exported.
			 * In that case the override method or property should be implemented in the parent class
			 * Affected game: Rimworld, Subnautica, Pillars of Eternity II
			 * TODO: : Add constructor when parent does not contain a default constructor and type in inherits from a system or unity type
			 * Affected game: Subnautica
			 */
			if (Definition == null)
			{
				methodList = new ScriptExportMethod[0];
				propertyList = new ScriptExportProperty[0];
				return;
			}
			List<MethodDefinition> abstractParentMethods = new List<MethodDefinition>();
			TypeReference baseType = Definition.BaseType;
			while(baseType != null)
			{
				TypeDefinition definition = baseType.Resolve();
				if(definition.Module.Name.StartsWith("UnityEngine.") || definition.Module.Name == "mscorlib.dll" || definition.Module.Name == "netstandard.dll")
				{
					foreach (MethodDefinition method in definition.Methods)
					{
						if (method.IsAbstract)
						{
							abstractParentMethods.Add(method);
						}
					}
				}
				baseType = definition.BaseType;
			}
			List<MethodDefinition> overrideMethods = new List<MethodDefinition>();
			foreach (MethodDefinition method in abstractParentMethods)
			{
				MethodDefinition overrideMethod = MetadataResolver.GetMethod(Definition.Methods, method);
				if(overrideMethod != null) overrideMethods.Add(overrideMethod);
			}
			List<ScriptExportMethod> methods = new List<ScriptExportMethod>();
			HashSet<PropertyDefinition> overrideProperties = new HashSet<PropertyDefinition>();
			foreach (MethodDefinition method in overrideMethods)
			{
				if (method.IsSetter)
				{
					overrideProperties.Add(Definition.Properties.First(prop => prop.SetMethod == method));
				}
				else if (method.IsGetter)
				{
					overrideProperties.Add(Definition.Properties.First(prop => prop.GetMethod == method));
				}
				else
				{
					ScriptExportMethod emethod = manager.RetrieveMethod(method);
					methods.Add(emethod);
				}
			}
			List<ScriptExportProperty> properties = new List<ScriptExportProperty>();
			foreach (PropertyDefinition property in overrideProperties)
			{
				ScriptExportProperty eproperty = manager.RetrieveProperty(property);
				properties.Add(eproperty);
			}
			methodList = methods.ToArray();
			propertyList = properties.ToArray();
		}

		private static bool IsContainsGenericParameter(TypeReference type)
		{
			if (type.IsGenericParameter)
			{
				return true;
			}
			if (type.IsArray)
			{
				return IsContainsGenericParameter(type.GetElementType());
			}
			if (type.IsGenericInstance)
			{
				GenericInstanceType instance = (GenericInstanceType)type;
				foreach (TypeReference argument in instance.GenericArguments)
				{
					if (IsContainsGenericParameter(argument))
					{
						return true;
					}
				}
			}
			return false;
		}

		public override string FullName { get; }
		public override string NestedName { get; }
		public override string CleanNestedName { get; }
		public override string TypeName { get; }
		public override string Namespace => DeclaringType == null ? Type.Namespace : DeclaringType.Namespace;
		public override string Module { get; }

		public override ScriptExportType DeclaringType => m_declaringType;
		public override ScriptExportType Base => m_base;

		public override IReadOnlyList<ScriptExportField> Fields => m_fields;
		public override IReadOnlyList<ScriptExportMethod> Methods => m_methods;
		public override IReadOnlyList<ScriptExportProperty> Properties => m_properties;


		protected override string Keyword
		{
			get
			{
				if (Definition == null)
				{
					return PublicKeyWord;
				}

				if (Definition.IsPublic || Definition.IsNestedPublic)
				{
					return PublicKeyWord;
				}
				if (Definition.IsNestedPrivate)
				{
					return PrivateKeyWord;
				}
				if (Definition.IsNestedFamily)
				{
					return ProtectedKeyWord;
				}
				return InternalKeyWord;
			}
		}
		protected override bool IsStruct => Type.IsValueType;
		protected override bool IsSerializable => Definition == null ? false : Definition.IsSerializable;
		public override bool IsPrimative => MonoType.IsCPrimitive(Type);

		private TypeReference Type { get; }
		private TypeDefinition Definition { get; }

		private ScriptExportType m_declaringType;
		private ScriptExportType m_base;
		private IReadOnlyList<ScriptExportField> m_fields;
		private IReadOnlyList<ScriptExportMethod> m_methods;
		private IReadOnlyList<ScriptExportProperty> m_properties;
	}
}

﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace AW.Helper
{
	public class ValueTypeWrapper<T>
	{
		public T Value { get; set; }

		public static List<ValueTypeWrapper<T>> CreateWrapperForBinding(IEnumerable<T> values)
		{
			return values.Select(data => new ValueTypeWrapper<T> {Value = data}).ToList();
		}
	}

	public class ReadonlyValueTypeWrapper<T>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="T:System.Object"/> class.
		/// </summary>
		public ReadonlyValueTypeWrapper(T value)
		{
			Value = value;
		}

		public T Value { get; private set; }

		public static IEnumerable<ReadonlyValueTypeWrapper<T>> CreateWrapperForBinding(IEnumerable<T> values)
		{
			return values.Select(data => new ReadonlyValueTypeWrapper<T>(data)).ToList();
		}
	}

	public class ValueTypeWrapper
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="T:System.Object"/> class.
		/// </summary>
		public ValueTypeWrapper(object value)
		{
			Value = value;
		}

		public object Value { get; private set; }

		public static List<ValueTypeWrapper> CreateWrapperForBinding(IEnumerable values)
		{
			return values.Cast<object>().Select(data => new ValueTypeWrapper(data)).ToList();
		}
	}

	public static class GeneralHelper
	{
		/// <summary>
		/// 	Used for converting any spaces in the string version of an enum name to a substitute in the CLR enum name.
		/// </summary>
		/// <example>
		/// 	In_Progress -to- "In Progress"
		/// </example>
		public static readonly char EnumSpaceSubstitute = '_';

		public static readonly char EnumNumberPrefix = '_';
		public const string StringJoinSeperator = ", ";

		#region Debuging

		/// <summary>
		/// 	Sends a msg to the Win32 debug output and prefixs it with the name off the method that called TraceOut
		/// </summary>
		/// <param name = "msg">The message.</param>
		public static void TraceOut(string msg)
		{
			Trace.WriteLine(new StackTrace(false).GetFrame(1).GetMethod().Name + ": " + msg);
		}

		public static void DebugOut(string msg)
		{
			Debug.WriteLine(new StackTrace(false).GetFrame(1).GetMethod().Name + ": " + msg);
		}

		#endregion

		public static void ApplicationOutputLogLine(string lineToLog, string source, bool isVerboseMessage, bool appendNewLine)
		{
			if (appendNewLine)
				Trace.WriteLine(lineToLog);
			else
				Trace.Write(lineToLog);
		}

		/// <summary>
		/// 	Converts the string to the enum.
		/// </summary>
		/// <typeparam name = "T"></typeparam>
		/// <param name = "strOfEnum">The string version of the enum.</param>
		/// <returns></returns>
		public static T ToEnum<T>(this string strOfEnum)
		{
			return String.IsNullOrEmpty(strOfEnum) ? default(T) : strOfEnum.ToEnum<T>(EnumSpaceSubstitute);
		}

		/// <summary>
		/// 	Converts the string to the enum.
		/// </summary>
		/// <typeparam name = "T"></typeparam>
		/// <param name = "strOfEnum">The string version of the enum.</param>
		/// <param name = "spaceSubstitute">The space substitute.</param>
		/// <returns></returns>
		public static T ToEnum<T>(this string strOfEnum, char spaceSubstitute)
		{
			var fixedString = strOfEnum.Replace(' ', spaceSubstitute);
			if (Char.IsDigit(fixedString, 0))
			{
				int enumindex;
				if (Int32.TryParse(fixedString, out enumindex))
					return (T) Enum.ToObject(typeof (T), enumindex);
				fixedString = EnumNumberPrefix + fixedString;
			}
			return (T) Enum.Parse(typeof (T), fixedString, true);
		}

		public static string JoinAsString<T>(this IEnumerable<T> input)
		{
			return JoinAsString(input, StringJoinSeperator);
		}

		public static string JoinAsString<T>(this IEnumerable<T> input, string seperator)
		{
			var ar = input.Select(i => i.ToString()).Where(s => !String.IsNullOrEmpty(s)).ToArray();
			return String.Join(seperator, ar);
		}

		public static IEnumerable<T> SkipTake<T>(this IEnumerable<T> superset, int pageIndex, int pageSize)
		{
			return superset.Skip(pageIndex*pageSize).Take(pageSize);
		}

		public static int GetPageCount(int pageSize, int totalItemCount)
		{
			if (totalItemCount > 0)
				return (int) Math.Ceiling(totalItemCount/(double) pageSize);
			return 0;
		}

		public static DataTable CopyToDataTable(this IEnumerable source)
		{
			var dataView = source as DataView;
			if (dataView != null && dataView.Table != null)
				return dataView.Table;
			return new ObjectShredder(MetaDataHelper.GetPropertiesToSerialize).Shred(source, null, null);
		}

		public static DataTable StripTypeColumns(this DataTable source)
		{
			var dataColumnsToRemove = source.Columns.OfType<DataColumn>().Where(dc => !dc.DataType.IsSerializable || dc.DataType == typeof (Type)).ToList();
			foreach (var dataColumn in dataColumnsToRemove)
				source.Columns.Remove(dataColumn);
			return source;
		}

		public static DataTable StripNonSerializables(this DataTable source)
		{
			foreach (var dataColumn in source.Columns.OfType<DataColumn>().Where(dc => dc.DataType == typeof (object)))
				foreach (var row in source.Rows.OfType<DataRow>().Where(row => row[dataColumn] != null && !row[dataColumn].GetType().IsSerializable))
					row[dataColumn] = null;
			return source;
		}

		/// <summary>
		/// 	Copies enumerable to a data table.
		/// </summary>
		/// <see cref = "http://msdn.microsoft.com/en-us/library/bb669096.aspx" />
		/// <typeparam name = "T"></typeparam>
		/// <param name = "source">The source.</param>
		/// <returns></returns>
		public static DataTable CopyToDataTable<T>(this IEnumerable<T> source)
		{
			return CopyToDataTable(source, null, null);
		}

		public static DataTable CopyToDataTable<T>(this IEnumerable<T> source, DataTable table, LoadOption? options)
		{
			return new ObjectShredder(MetaDataHelper.GetPropertiesToSerialize).Shred(source, table, options);
		}

		/// <summary>
		/// 	Convert a List{T} to a DataTable.
		/// </summary>
		public static DataTable ToDataTable<T>(List<T> items)
		{
			var tb = new DataTable(typeof (T).Name);
			var props = typeof (T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

			foreach (var prop in props)
			{
				var t = MetaDataHelper.GetCoreType(prop.PropertyType);
				tb.Columns.Add(prop.Name, t);
			}

			foreach (var item in items)
			{
				var values = new object[props.Length];
				for (var i = 0; i < props.Length; i++)
				{
					values[i] = props[i].GetValue(item, null);
				}
				tb.Rows.Add(values);
			}
			return tb;
		}

		/// <summary>
		/// returns null if empty.
		/// </summary>
		/// <see cref="http://haacked.com/archive/2010/06/16/null-or-empty-coalescing.aspx"/>
		/// <param name="items">The items.</param>
		/// <returns></returns>
		public static IEnumerable<T> AsNullIfEmpty<T>(this IEnumerable<T> items)
		{
			return items == null || !items.Any() ? null : items;
		}

		/// <summary>
		/// Determines whether the specified IEnumerable is null or empty.
		/// </summary>
		/// <see cref="http://haacked.com/archive/2010/06/10/checking-for-empty-enumerations.aspx"/>
		/// <typeparam name="T"></typeparam>
		/// <param name="items">The items.</param>
		/// <returns>
		/// 	<c>true</c> if specified IEnumerable is null or empty; otherwise, <c>false</c>.
		/// </returns>
		public static bool IsNullOrEmpty<T>(this IEnumerable<T> items)
		{
			if (items is ICollection)
				return ((ICollection) items).Count == 0;
			return items.AsNullIfEmpty() == null;
		}

		public static List<ValueTypeWrapper<string>> CreateStringWrapperForBinding(this StringCollection strings)
		{
			return strings == null ? null : ValueTypeWrapper<string>.CreateWrapperForBinding(strings.Cast<string>());
		}

		#region Settings

		public static bool HasSetting(ApplicationSettingsBase applicationSettingsBase, string settingName)
		{
			return HasSettingsAndName(applicationSettingsBase, settingName)
			       && (applicationSettingsBase.Properties.OfType<SettingsProperty>().Any(sp => sp.Name == settingName)
			           || applicationSettingsBase.PropertyValues.OfType<SettingsPropertyValue>().Any(pv => pv.Name == settingName));
		}

		private static bool HasSettingsAndName(ApplicationSettingsBase applicationSettingsBase, string settingName)
		{
			return !String.IsNullOrEmpty(settingName) && applicationSettingsBase != null;
		}

		public static SettingsPropertyValue GetSetting(ApplicationSettingsBase applicationSettingsBase, string settingName)
		{
			if (HasSetting(applicationSettingsBase, settingName))
			{
				var settingsPropertyValue = applicationSettingsBase.PropertyValues[settingName];
				if (settingsPropertyValue == null)
				{
					applicationSettingsBase[settingName] = applicationSettingsBase[settingName]; //To force load
					settingsPropertyValue = applicationSettingsBase.PropertyValues[settingName];
				}
				return settingsPropertyValue;
			}
			return null;
		}

		public static SettingsPropertyValue GetSetting(ApplicationSettingsBase applicationSettingsBase, string settingName, Type settingType)
		{
			var settingsPropertyValue = GetSetting(applicationSettingsBase, settingName);
			if (settingsPropertyValue == null && HasSettingsAndName(applicationSettingsBase, settingName))
			{
				AddSettingsProperty(applicationSettingsBase, settingName, settingType);
				settingsPropertyValue = GetSetting(applicationSettingsBase, settingName);
			}
			return settingsPropertyValue;
		}

		public static void AddSettingsProperty(ApplicationSettingsBase applicationSettingsBase, string settingName, Type settingType)
		{
			var settingsProperty = new SettingsProperty(settingName)
			                       	{
			                       		PropertyType = settingType,
			                       		Provider = applicationSettingsBase.Providers.Cast<SettingsProvider>().FirstOrDefault(),
			                       		SerializeAs = SettingsSerializeAs.Xml
			                       	};
			settingsProperty.Attributes.Add(typeof (UserScopedSettingAttribute), new UserScopedSettingAttribute());
			applicationSettingsBase.Properties.Add(settingsProperty);
		}

		#endregion
	}
}
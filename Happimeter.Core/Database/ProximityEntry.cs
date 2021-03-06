﻿using System;
using SQLite;

namespace Happimeter.Core.Database
{
	public class ProximityEntry
	{
		public ProximityEntry()
		{
		}

		[PrimaryKey, AutoIncrement]
		public int Id { get; set; }
		public double Average { get; set; }
		[Indexed]
		public DateTime Timestamp { get; set; }
		[Indexed]
		public int CloseToUserId { get; set; }
		/// <summary>
		/// Can be Rssi Or Cm
		/// </summary>
		/// <value>The type of the proximity.</value>
		public string ProximityType { get; set; }
		public string CloseToUserIdentifier { get; set; }
	}
}

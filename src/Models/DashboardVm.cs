using MT.Data;
using System;
using System.Collections.Generic;

public class DashboardVm
{
    // KPIs
    public int TodayBooking { get; set; }
    public int TotalApproval { get; set; }
    public int TodayRejected { get; set; }
    public int TotalBooking { get; set; }

    // Recent 10
    public List<VehicleRegistration> Recent { get; set; } = new();
}

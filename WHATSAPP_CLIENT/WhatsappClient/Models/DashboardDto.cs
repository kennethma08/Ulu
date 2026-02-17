using System;
using System.Collections.Generic;

namespace WhatsappClient.Models
{
    public class DashboardDto
    {
        public int TotalConversations { get; set; }
        public int NewClients { get; set; }
        public int AvgFirstResponseSeconds { get; set; }
        public string AvgFirstResponseDisplay { get; set; } = "0s";

        // Gráfico: mensajes por mes
        public List<string> MonthlyMessagesLabels { get; set; } = new();
        public List<int> MonthlyMessagesValues { get; set; } = new();

        // Canales (solo WhatsApp)
        public int TotalMessages { get; set; }

        // Actividad reciente (solo 2 tipos)
        public List<ActivityItem> Activity { get; set; } = new();
    }

    public class ActivityItem
    {
        // "new_client" | "conv_closed"
        public string Type { get; set; } = "new_client";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public DateTime When { get; set; }
    }
}

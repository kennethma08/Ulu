using System;

namespace Whatsapp_API.BotFlows.Core
{
    public class FlowInput
    {
        public int CompanyId { get; set; }
        public int ContactId { get; set; }
        public int ConversationId { get; set; }
        public string PhoneE164 { get; set; } = "";
        public string MessageType { get; set; } = "text";
        public string MessageText { get; set; } = "";
        public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
        public bool JustCreated { get; set; } = false;
    }
}

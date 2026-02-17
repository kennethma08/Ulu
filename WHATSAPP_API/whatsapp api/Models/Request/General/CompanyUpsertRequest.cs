namespace Whatsapp_API.Models.Request.General
{
    public class CompanyUpsertRequest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? FlowKey { get; set; }
    }
}

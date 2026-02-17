namespace Whatsapp_API.Models.Entities.System
{
    public class WhatsappTemplate
    {
        public int Id { get; set; }
        public int CompanyId { get; set; } 
        public string Name { get; set; } = ""; 
        public string Language { get; set; } = "es"; 
        public string? TemplateIdentifier { get; set; } //VER SI SE PUEDE CAMBIAR EL NOMBRE, ESTE ATRIBUTO EN SI NO ES CATEGORIA, ESTE ATRIBUTO SE ESTA USANDO PARA LLAMAR A LA PLANTILLA EN EL CODIGO, SE LLAMA CON EL NOMBRE QUE SE PONE ACA, REVISAR ESO
        public int BodyParamCount { get; set; }      // cuántas variables admite el body, POR EJEMPLO SI ES UNA UBICACION QUE NECESITA VARIAS
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}

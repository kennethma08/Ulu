namespace Whatsapp_API.Models.Helpers
{
    // respuesta simple para decir si salió bien o no
    public class DescriptiveBoolean
    {
        public bool Sucessfull { get; set; }
        public string Message { get; set; } = "";
        public int StatusCode { get; set; } = 200;

        public bool Exitoso { get => Sucessfull; set => Sucessfull = value; }
        public string Mensaje { get => Message; set => Message = value; }

        public bool Successful { get => Sucessfull; set => Sucessfull = value; }
    }

    // Genérico con alias: Data/Object/Objeto apuntan al mismo campo
    public class DescriptiveBoolean<T> : DescriptiveBoolean
    {
        private T? _data;
        public T? Data { get => _data; set => _data = value; }
        public T? Object { get => _data; set => _data = value; }
        public T? Objeto { get => _data; set => _data = value; }
    }

    public class BooleanoDescriptivo : DescriptiveBoolean { }
    public class BooleanoDescriptivo<T> : DescriptiveBoolean<T> { }
}

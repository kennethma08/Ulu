using Whatsapp_API.Models.Helpers;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Web;

namespace Whatsapp_API.Helpers
{

    public class WebMethods
    {
        public string Get(string url)
        {
            var message = "";

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(url);
                var responseTask = client.GetAsync("");
                responseTask.Wait();

                var result = responseTask.Result;

                if (result.IsSuccessStatusCode)
                {
                    var readTask = result.Content.ReadAsStringAsync();
                    readTask.Wait();

                    message = readTask.Result;
                }
                return message;
            }
        }

        public string Get(int? id, string url)
        {
            var json = "";

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(url);
                var responseTask = client.GetAsync("" + id);
                responseTask.Wait();

                var result = responseTask.Result;

                if (result.IsSuccessStatusCode)
                {
                    var readTask = result.Content.ReadAsStringAsync();
                    readTask.Wait();

                    json = readTask.Result;
                }
                return json;
            }
        }

        public string Get(string pNombre, string url)
        {
            var json = "";

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(url);
                var responseTask = client.GetAsync("" + pNombre);
                responseTask.Wait();

                var result = responseTask.Result;

                if (result.IsSuccessStatusCode)
                {
                    var readTask = result.Content.ReadAsStringAsync();
                    readTask.Wait();

                    json = readTask.Result;
                }
                return json;
            }
        }

        public static async Task<BooleanoDescriptivo<O>> Get<O>(string url, object parametros = null)
        {
            try
            {
                string urlParams = "";
                if (parametros != null)
                {
                    //Convertir objeto en los parametros de URL
                    string paso1 = JsonConvert.SerializeObject(parametros);
                    IDictionary<string, string> paso2 = JsonConvert.DeserializeObject<IDictionary<string, string>>(paso1);
                    IEnumerable<string> paso3 = paso2.Select(x => HttpUtility.UrlEncode(x.Key) + "=" + HttpUtility.UrlEncode(x.Value));
                    urlParams = "?" + string.Join("&", paso3);
                }

                using (var client = new HttpClient())
                {
                    Uri uri = new Uri(url + urlParams);

                    HttpResponseMessage response = await client.GetAsync(uri);
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    if (typeof(O) == typeof(string))
                    {
                        jsonResponse = "'" + jsonResponse + "'";
                    }
                    O objectResponse = JsonConvert.DeserializeObject<O>(jsonResponse);

                    string mensaje;
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.Unauthorized:
                            mensaje = "No se pudo realizar la autorización con el servidor.";
                            break;
                        case HttpStatusCode.InternalServerError:
                            mensaje = "Error interno del servidor.";
                            break;
                        case HttpStatusCode.NotFound:
                            mensaje = "Url no encontrada";
                            break;
                        default:
                            mensaje = "Request realizado";
                            break;
                    }

                    return new BooleanoDescriptivo<O>
                    {
                        Exitoso = response.IsSuccessStatusCode,
                        Mensaje = mensaje,
                        Objeto = objectResponse
                    };
                }
            }
            catch (Exception ex)
            {
                return new BooleanoDescriptivo<O> { Exitoso = false, Mensaje = "Error en la comunicación con el servidor." };
            }
        }

        public bool Post(string jsonObj, string url)
        {
            using (var client = new HttpClient())
            {
                var content = new StringContent(jsonObj, Encoding.UTF8, "application/json");

                var uri = new Uri(url);

                var responseTask = client.PostAsync(uri, content);

                var result = responseTask.Result;

                return result.IsSuccessStatusCode;
            }
        }

        public static async Task<BooleanoDescriptivo<O>> Post<I, O>(string url, I objeto)
        {
            try
            {
                string json = JsonConvert.SerializeObject(objeto);

                using (var client = new HttpClient())
                {
                    StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
                    Uri uri = new Uri(url);

                    HttpResponseMessage response = await client.PostAsync(uri, content);
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    if (typeof(O) == typeof(string))
                    {
                        jsonResponse = "'" + jsonResponse + "'";
                    }
                    O objectResponse = JsonConvert.DeserializeObject<O>(jsonResponse);

                    string mensaje;
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.Unauthorized:
                            mensaje = "No se pudo realizar la autorización con el servidor.";
                            break;
                        case HttpStatusCode.InternalServerError:
                            mensaje = "Error interno del servidor.";
                            break;
                        case HttpStatusCode.NotFound:
                            mensaje = "Url no encontrada";
                            break;
                        default:
                            mensaje = "Request realizado";
                            break;
                    }

                    return new BooleanoDescriptivo<O>
                    {
                        Exitoso = response.IsSuccessStatusCode,
                        Mensaje = mensaje,
                        Objeto = objectResponse
                    };
                }
            }
            catch (Exception ex)
            {
                return new BooleanoDescriptivo<O> { Exitoso = false, Mensaje = "Error en la comunicación con el servidor." };
            }
        }

        public bool Post(string url, Dictionary<string, string> parametros)
        {
            var client = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(parametros) };
            var responseTask = client.SendAsync(req);
            var result = responseTask.Result;
            return result.IsSuccessStatusCode;
        }

        public bool Post(string url)
        {
            using (var client = new HttpClient())
            {
                var uri = new Uri(url);

                var responseTask = client.PostAsync(uri, null);

                responseTask.Wait();
                var result = responseTask.Result;

                return result.IsSuccessStatusCode;
            }
        }

        public string PostResponseString(string jsonObj, string url)
        {
            using (var client = new HttpClient())
            {
                var content = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");

                var uri = new Uri(url);

                var responseTask = client.PostAsync(uri, content);

                responseTask.Wait();
                var result = responseTask.Result;
                var mensajeAsync = result.Content.ReadAsStringAsync();
                var mensaje = mensajeAsync.Result.ToString();
                return mensaje;
            }
        }

        public bool Put(string jsonObj, string url)
        {
            using (var client = new HttpClient())
            {
                var content = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");

                var uri = new Uri(url);

                var responseTask = client.PutAsync(uri, content);

                responseTask.Wait();
                var result = responseTask.Result;

                return result.IsSuccessStatusCode;
            }
        }

        public bool Delete(int id, string url)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(url);
                var responseTask = client.DeleteAsync("" + id);

                responseTask.Wait();
                var result = responseTask.Result;

                return result.IsSuccessStatusCode;
            }
        }

        public string GetForWebClient(string url, string id)
        {
            using (var client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;

                Uri uriConsultarRespuesta = new Uri(url);

                var result = client.DownloadString(uriConsultarRespuesta + id);
                return result;

                //return JsonConvert.DeserializeObject<EntitiesMH.Mensajes.Consulta>(result);
            }
        }
    }


}


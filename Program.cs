using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;

namespace AkBarsTest
{
    class Program
    {
        static string sourceDirName; //имя директории, откуда надо взять файлы
        static string destinationPath; //путь на диске Яндекса для файлов. Для простоты считаем, что это полный путь от корня
        static string baseHost = @"https://cloud-api.yandex.net/v1/"; //базовая часть всех url запросов
        static string uriForGettingUploaderUrl = "disk/resources/upload";
        static string uriForGettingOperationStatus = "disk/operations/";
        static string auothKey;
        static HttpClient client;
        static string operationIdPattern = "\"operation_id\": *\"(?<op>[^\"]+)\",";
        static string uploaderUrlPattern = "\"href\": *\"(?<href>[^\"]+)\",";
        static string operationStatusPattern = "\"[^\"]+\": *\"(?<status>[^\"]+)\",";
        static Regex operationIdRE = new Regex(operationIdPattern);
        static Regex uploaderUrlRE = new Regex(uploaderUrlPattern);
        static Regex operationStatusRE = new Regex(operationStatusPattern);

        static string separator = "=======================================";
        static async Task Main(string[] args)
        {
            if(args.Count() < 2) //2 аргумента должны быть обязательно
            {
                System.Console.WriteLine("Too few arguments. There must be 2 arguments.");
                return;
            }
            sourceDirName = args[0];
            destinationPath = args[1]; //TODO: можно добавить проверку, что первым идет слэш

            /*
             Посколько для программного получения ключа аутентификации надо регистрировать свое приложение в яндексе, а также 
             потому, что в тестовом задании ссылка ведет на полигон, где можно сразу вручную получить свой ключ для тестирования,
             то я решил не "заморачиваться" и пойти простым путем:позволить себе добавить еще один возможный аргумент командной строки для 
             этого приложения: Ваш собственный ключ аутентификации. Либо можно просто захардкодить Ваш ключ в тексте программы, для этого тогда 
             понадобится перекомпиляция, конечно.
             Надеюсь, это не будет считаться серьезной ошибкой или недостатком для простого тестового задания.
             */
            auothKey = "AgAAAAAWuyxgAADLW4iw9yl2q0TZr4FK9AhCA9k";// args[2];

            
            System.Console.WriteLine("Папка-источник файлов: {0}", sourceDirName);
            System.Console.WriteLine("Путь для загрузки файлов: {0}", destinationPath);
            System.Console.WriteLine(separator);

            var files = System.IO.Directory.EnumerateFiles(sourceDirName);

            if (files.Count() == 0)
            {
                System.Console.WriteLine("Папка-источник не содержит файлов");
                return;
            }

            client = new HttpClient();
            //список асинхронно выполняемых задач-аплоудеров файлов
            List<Task<string>> tasks = new List<Task<string>>();
            foreach (var file in files)
            {
                Console.WriteLine("Начало загрузки файла: {0}", file);
                 var tsk = aploadFile(file);
                //var tsk = fileUpload(file);
                tasks.Add(tsk);
            }

            Console.WriteLine("Подождите окончания загрузки...");

            while (tasks.Any())
            {
                Task<string> finishedTask = await Task.WhenAny(tasks);
                string mes = await finishedTask;
                Console.WriteLine( mes);
                tasks.Remove(finishedTask);
            }

            Console.WriteLine("Загрузка файлов закончена!");
        }


        static async Task<string> fileUpload(string file)
        {
            //Console.WriteLine("start uploading file: {0}",file);
            int numChars = file.Count();
            return await Task.Run(() =>
            {
                // return DoWork();
               // Thread.Sleep(numChars * 50);
                //Console.WriteLine("done uploading file: {0}", file);
                return "done with the file " + file;
            });
        }


        static async Task<string> aploadFile(string fullFileName)
        {
            string file = System.IO.Path.GetFileName(fullFileName);
            string pathOnDisk = string.Format("{0}/{1}", destinationPath, file);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth",auothKey);
            var url = string.Format("{0}{1}?path={2}", baseHost,uriForGettingUploaderUrl, pathOnDisk);
            try
            {
                HttpResponseMessage getResponse = await client.GetAsync(url);
                getResponse.EnsureSuccessStatusCode();
                string responseBody = await getResponse.Content.ReadAsStringAsync();
                MatchCollection operationIdMatches = operationIdRE.Matches(responseBody);
                MatchCollection uploaderUrlMatches = uploaderUrlRE.Matches(responseBody);
                string operationId = operationIdMatches.Count > 0 ? operationIdMatches[0].Groups["op"].Value : responseBody;
                string uploaderUrl = uploaderUrlMatches.Count > 0 ? uploaderUrlMatches[0].Groups["href"].Value : responseBody;

                HttpResponseMessage putResponse = await client.PutAsync(uploaderUrl, new StreamContent(new FileStream(fullFileName,FileMode.Open)));
                putResponse.EnsureSuccessStatusCode();

                url = string.Format("{0}{1}{2}", baseHost, uriForGettingOperationStatus, operationId);
                HttpResponseMessage operationStatus = await client.GetAsync(url);
                operationStatus.EnsureSuccessStatusCode();

                responseBody = await operationStatus.Content.ReadAsStringAsync();
                MatchCollection operationStatusMatches = operationStatusRE.Matches(responseBody);
                string status = operationStatusMatches.Count > 0 ? operationStatusMatches[0].Groups["status"].Value : responseBody;
                return string.Format("файл: {0}, статус: {1}",file,status);

            }
            catch (HttpRequestException e)
            {
                string errorMessage = string.Format("При загрузке файла {0} было вызвано исключение: {1}", fullFileName,e.Message);
                return errorMessage;
            }
        }
    }

}


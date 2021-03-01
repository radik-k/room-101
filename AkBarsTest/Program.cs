using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;
/*
 К сожалению, не успел сделать отмену (зависшей) операции и наверное еще каких-то "неприятных" ситуаций.
И так уже слишком затянул с выполнением этого задания.
Наверное, можно было так же сделать код более изящным с "архитектурной" точки зрения, но слова о том, 
что на эту программу достаточно 2-3 часов заставили меня думать больше о скорости написания кода.
(Посколько скорость написания не самое последнее по важности умение.)
 */
namespace AkBarsTest
{
    class Program
    {
        static string sourceDirName; //имя директории, откуда надо взять файлы
        static string destinationPath; //путь на диске Яндекса для файлов. Для простоты считаем, что это полный путь от корня, со слешем
        static string baseHost = @"https://cloud-api.yandex.net/v1/"; //базовая часть всех url запросов
        static string uriForGettingOrCreatingDestinationFolder = "disk/resources";
        static string uriForGettingUploaderUrl = "disk/resources/upload";
        static string uriForGettingOperationStatus = "disk/operations/";
        static string auothKey;

        static HttpClient client;

        //для разбора JSON ответа для получения id операции...
        static string operationIdPattern = "\"operation_id\": *\"(?<op>[^\"]+)\",";
        static Regex operationIdRE = new Regex(operationIdPattern);

        //...ссылки на загрузчика...
        static string uploaderUrlPattern = "\"href\": *\"(?<href>[^\"]+)\",";
        static Regex uploaderUrlRE = new Regex(uploaderUrlPattern);

        //... статуса операции
        static string operationStatusPattern = "\"[^\"]+\" *: *\"(?<status>[^\"]+)\"";        
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
            destinationPath = args[1]; //работает как со слэшем впереди, так и без него.

            /*
             Посколько для программного получения ключа аутентификации надо регистрировать свое приложение в яндексе, а также 
             потому, что в тестовом задании ссылка ведет на полигон, где можно сразу вручную получить свой ключ для тестирования,
             то я решил не "заморачиваться" и пойти простым путем:позволить себе добавить еще один возможный аргумент командной строки для 
             этого приложения: Ваш собственный ключ аутентификации. Либо можно просто захардкодить Ваш ключ в тексте программы, для этого тогда 
             понадобится перекомпиляция, конечно.
             Надеюсь, это не будет считаться серьезной ошибкой или недостатком для простого тестового задания.
             */
            auothKey = args[2];

            
            System.Console.WriteLine("Папка-источник файлов: {0}", sourceDirName);
            System.Console.WriteLine("Путь для загрузки файлов: {0}", destinationPath);
            System.Console.WriteLine(separator);

            var files = System.IO.Directory.EnumerateFiles(sourceDirName);

            if (files.Count() == 0)
            {
                System.Console.WriteLine("Папка-источник не содержит файлов");
                return;
            }

            //подготовим клиента, которого будем использовать во всех запросах
            client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", auothKey);

            //список асинхронно выполняемых задач-аплоудеров файлов
            List<Task<string>> tasks = new List<Task<string>>();
            foreach (var file in files)
            {
                Console.WriteLine("Начало загрузки файла: {0}", file);
                var tsk = aploadFile(file);
                tasks.Add(tsk);
            }

            Console.WriteLine("Подождите окончания загрузки...");

            //по мере выполнения задач список уменьшается, пока не опустеет полностью.
            while (tasks.Any())
            {
                Task<string> finishedTask = await Task.WhenAny(tasks);
                string mes = await finishedTask;
                Console.WriteLine( mes);
                tasks.Remove(finishedTask);
            }

            Console.WriteLine("Загрузка файлов закончена!");
        }


        static async Task<string> aploadFile(string fullFileName)
        {
            //получим имя файла, без его пути
            string file = System.IO.Path.GetFileName(fullFileName);
            //добавим имя файла к папке на яндекс диске, указанной во втором аргументе
            string pathOnYandexDisk = string.Format("{0}/{1}", destinationPath, file);

            //сначала создаем папку на яндекс диске, которая передана во втором аргументе нашей программы
            var url = string.Format("{0}{1}?path={2}", baseHost, uriForGettingOrCreatingDestinationFolder, destinationPath);
            try 
            {
                HttpResponseMessage getResponse = await client.PutAsync(url,null);
                getResponse.EnsureSuccessStatusCode();
            }
            catch(HttpRequestException) //если папка уже существует, то будет выброшено исключение, которое мы ловим
            { }

            //получим ссылку на загрузчика файлов
            //для этого отправим GET запрос:

            url = string.Format("{0}{1}?path={2}", baseHost,uriForGettingUploaderUrl, pathOnYandexDisk);
            try
            {
                HttpResponseMessage response = await client.GetAsync(url);                
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                MatchCollection operationIdMatches = operationIdRE.Matches(responseBody);
                MatchCollection uploaderUrlMatches = uploaderUrlRE.Matches(responseBody);
                string operationId = operationIdMatches.Count > 0 ? operationIdMatches[0].Groups["op"].Value : responseBody;

                //получим из ответа яндекса ссылку на загрузчика:
                if(uploaderUrlMatches.Count == 0)
                {
                    return string.Format("не удалось получить ссылку для загрузки файла {0}", fullFileName);
                }                   
                string uploaderUrl = uploaderUrlMatches[0].Groups["href"].Value;

                //загружаем файл на яндекс диск
                response = await client.PutAsync(uploaderUrl, new StreamContent(new FileStream(fullFileName, FileMode.Open)));
                response.EnsureSuccessStatusCode();

                //получим статус операции:
                url = string.Format("{0}{1}{2}", baseHost, uriForGettingOperationStatus, operationId);
                response = await client.GetAsync(url);
                responseBody = await response.Content.ReadAsStringAsync();
                MatchCollection operationStatusMatches = operationStatusRE.Matches(responseBody);
                string status = operationStatusMatches.Count > 0 ? operationStatusMatches[0].Groups["status"].Value : "не удалось получить статус операции";
                return string.Format("файл: {0}, статус: {1}", file, status); 
            }
            catch (HttpRequestException e)
            {
                string errorMessage = string.Format("При загрузке файла {0} было вызвано исключение: {1}", fullFileName,e.Message);
                return errorMessage;
            }
        }
    }

}


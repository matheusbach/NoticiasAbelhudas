using RssHelper.Util;
using System.Text;
using Telegram.Bot;
using Helpers;
using SQLite;

namespace NoticiasAbelhudas     // Notícias sendo publicadas em https://t.me/NoticiasAbelhudas
{
	public class Settings		// Configurações
	{
		public static readonly List<string> RssSearchList = new()
			{
				"abelha -alergia -alérgicos -ataque OR abelhas -alergia -alérgicos -ataque",
				"apicultura -alergia -alérgicos -ataque",
				"meliponicultura",
				"abelhas sem ferrão",
				"meliponas",
				"apis melifera -alergia -alérgicos -ataque"
			 };																				// lista de termos a serem posquisados no GoogleNews. Usar formatação de busca adequada

		public const string RssRegionalCode = "&?hl=pt-BR&gl=BR&ceid=BR:pt-419&hl=pt-BR";	// acesse https://news.google.com/rss e pegue a string que será adicionada em sua Url (veja a mudança na barra de endereço). Você pode deixar vaziu se quiser, mas dependendo de onde você rodar o software a configuração pode ficar indesejada

		public const string BotTokenApi = "0123456789:XXXXXXXXXXXXXXXXXXXXXX-XXXXXXXXXXXX"; // crie um bot Telegram usando o @BotFather

		public const long PostChatId = -1001793833397;										// ID de um chat onde o bot está. Pode ser grupos, super-grupos, chats privados ou canais em que o bot esteja como administrador
	}

	internal class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Noticias Abelhudas - START");

			var databasePath = "/NoticiasAbelhudasSQLite.db"; // define local para o arquivo de database SQLite

			SQLiteAsyncConnection db = new(databasePath);                                                                                    // cria conexão SQLite

			db.CreateTableAsync<RssPost>().Wait();                                                                                           // cria tabela caso não exista (CREATE TABLE IF NOT EXISTS)

			Console.WriteLine("Database inicializada - " + db.Table<RssPost>().CountAsync().Result + " noticias registradas");

			TelegramBotClient botClient = new(Settings.BotTokenApi);                                                                         // cria client de bot telegram

			Console.WriteLine("Telegram bot (@" + botClient.GetMeAsync().Result.Username + ") iniciado. => " + botClient.GetChatAsync(Settings.PostChatId).Result.Title);

			while (true)
			{
				List<string> urlList = new(Settings.RssSearchList.Select(termo => "https://news.google.com/rss/search?q=" + termo + Settings.RssRegionalCode)); // cria lista de RssFeed do GoogleNews com base na lista de termos para pesquisa

				List<Feed> RssResponseList = RssParserHelper.GetParsedFeed(urlList).Result.ToList();                                                            // Pega e analisa os Feeds Rss, mandando eles para uma lista

				RssResponseList.DistinctBy(rss => rss.Title).OrderBy(rss => rss.PublishDate).ToList().ForEach(rss =>                                            // Filtra pelos elementos diferentes, ordena por data e abre um ForEach
				{
					try
					{
						string realDetectedTitle = rss.Title.Replace(" | ", " - ").Split(" - ").Where(split => !split.Contains(".com") && !split.Contains(".gov")).OrderByDescending(split => split.Length).OrderByDescending(split => { return Settings.RssSearchList.Any(term => term.Split(' ').Where(term => !term.StartsWith('-')).Any(word => split.Contains(word))) ? 1 : 0; }).First(); // tenta determinar com base em heurísticas qual parte da frase representa o título real da notícia

						if (db.Table<RssPost>().Where(row => row.InternalID.Equals(rss.InternalID)).CountAsync().Result == 0 && db.Table<RssPost>().Where(row => row.Title.Contains(realDetectedTitle)).CountAsync().Result == 0)	// Verifica para caso não exista um Post igual já salvo na Database, pois só queremos processar Rss novos
						{
							Console.WriteLine(rss.PublishDate + " == " + rss.Title);

							string[] rssTitleSplit = rss.Title.Replace(" | ", " - ").Split(" - ");                                                              // Quebra o título em pedaços, pois a última parte geralmente é o nome do portal de notícias

							StringBuilder post = new();     // faz uma mensagem para mandar no telegram
							post.AppendLine('*' + realDetectedTitle.EscapeMD2() + '*');
							post.AppendLine();
							post.AppendLine("Fonte: [" + (rssTitleSplit.Count() > 1 ? rssTitleSplit.Last().EscapeMD2() : rss.FeedUrl.EscapeMD2()) + "](" + rss.FeedUrl.EscapeMD2() + ")");

							botClient.SendTextMessageAsync(
								chatId: Settings.PostChatId,
								text: post.ToString(),
								parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2
							).Wait(); // envia a mensagem para o chat de postagem telegram

							db.InsertAsync(new RssPost() { InternalID = rss.InternalID, Title = rss.Title, FeedUrl = rss.FeedUrl, PublishDate = rss.PublishDate }); // Insere informações do Post na database SQLite

							Thread.Sleep(10000);     // Aguarda 10 segundos para evitar cooldown do telegram por spam
						}
					}
					catch (Exception e) { Console.WriteLine("ERROR: " + e.Message); }
				});

				Thread.Sleep(1800000); // aguarda meia hora entre cada checagem por notícias
			}
		}
	}
}

namespace Helpers
{
	public class RssPost
	{
		// Define a tabela RssPost do SQLite

		[PrimaryKey]
		public string InternalID { get; set; }

		public string Title { get; set; }
		public string FeedUrl { get; set; }
		public DateTime PublishDate { get; set; }
	}

	public static class Extensions
	{
		// Função para que o telegram entenda os caracteres desejados coo caracteres normais, e não formatação MarkdownV2
		public static string EscapeMD2(this string text) => text.Replace(@"_", @"\_").Replace(@"*", @"\*").Replace(@"[", @"\[").Replace(@"]", @"\]").Replace(@"(", @"\(").Replace(@")", @"\)").Replace(@"~", @"\~").Replace(@"`", @"\`").Replace(@">", @"\>").Replace(@"#", @"\#").Replace(@"+", @"\+").Replace(@"-", @"\-").Replace(@"=", @"\=").Replace(@"|", @"\|").Replace(@"{", @"\{").Replace(@"}", @"\}").Replace(@".", @"\.").Replace(@"!", @"\!");
	}
}

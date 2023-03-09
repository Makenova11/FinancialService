using FinancialSettlementService.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTest
{
    public class Tests
    {
        /// <summary>
        /// HttpClient;
        /// </summary>
        private HttpClient _testClient;

        /// <summary>
        /// DbContext.
        /// </summary>
        private BankDbContext _bankDbContext;

        /// <summary>
        /// Стартовая и ожидаемая сумма у всех клиентов.
        /// </summary>
        private const decimal startBalance = 100;

        /// <summary>
        /// Количество клиентов.
        /// </summary>
        private const int clientsCount = 50;

        /// <summary>
        /// Количество потоков.
        /// </summary>
        private const int threadCount = 10;

        private const string TestDbConnection = "User ID=postgres;Password=postgres;Server=localhost;Port=5432;Database=TestDb;";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var appBuilder = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(collection =>
                {
                    var contextDescription = collection.SingleOrDefault(descriptor =>
                        descriptor.ServiceType == typeof(DbContextOptions<BankDbContext>));

                    if (contextDescription is not null)
                        collection.Remove(contextDescription);

                    collection.AddDbContext<BankDbContext>(optionsBuilder =>
                        optionsBuilder.UseNpgsql(TestDbConnection));
                });
            });
            _testClient = appBuilder.CreateClient();
            _bankDbContext = appBuilder.Services.CreateScope().ServiceProvider.GetService<BankDbContext>();
            _bankDbContext.Database.EnsureDeleted();
            _bankDbContext.Database.Migrate();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _testClient.Dispose();
            _bankDbContext.Database.EnsureDeleted();
            _bankDbContext.Dispose();
        }
        /// <summary>
        /// Тест начисляет каждому по 200 в каждом потоке и списывает по 50. 
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task DepositInto_NewClients_ReturnUpdatedBalance()
        {
            // Arrange
            SignUpClients();
            
            // Act
            var threads = new Thread[threadCount];
            var guidList = _bankDbContext.Clients.Select(x => x.Id).ToList();
            for (int i = 0; i < threadCount; i++)
            {
                threads[i] = new Thread(DepositAndWithDraw);
                threads[i].Start();
            }
            foreach (var thread in threads)
                thread.Join();

            var clientsBalance = await _bankDbContext.BalanceAccounts.Select(x => x.Balance).ToListAsync();

            // Assert
            clientsBalance.Should().OnlyContain(balance => balance == 1600);

            void DepositAndWithDraw()
            {
                var rand = new Random();

                foreach (var id in guidList)
                {
                    ExecutePatchQuery("deposit", id, 200);
                    ExecutePatchQuery("withdraw", id, 50);
                }
            }
        }

        /// <summary>
        /// Подготовка к тесту - создание 50 клиентов.
        /// </summary>
        private void SignUpClients()
        {
            var rand = new Random();
            for (int i = 0; i < clientsCount; i++)
            {
                var client = new Client
                {
                    FirstName = rand.Next(1, 90).ToString(),
                    SecondName = rand.Next(1, 90).ToString(),
                    BirthDay = new(2000, 10, 11, 0, 30, 0, DateTimeKind.Utc),
                    Patronymic = rand.Next(1, 90).ToString()
                };
                var balanceAccount = new BalanceAccount
                {
                    Balance = startBalance,
                    Client = client
                };

                _bankDbContext.Clients.Add(client);
                _bankDbContext.BalanceAccounts.Add(balanceAccount);
            }
            _bankDbContext.SaveChanges();
        }
    
        /// <summary>
        /// Сформировать запрос на изменение баланса.
        /// </summary>
        /// <param name="operation"> Тип операции с балансом. </param>
        /// <param name="clientId"> Идентификатор клиента. </param>
        /// <param name="amount"> Сумма изменения баланса. </param>
        private void ExecutePatchQuery(string operation, Guid clientId, decimal amount)
        {
            var result = _testClient.PatchAsync($"BalanceAccount/{operation}/{clientId}/{amount}", new StringContent("")).Result;
            result.EnsureSuccessStatusCode();
        }
    }
}
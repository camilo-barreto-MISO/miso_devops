using BlackListApi.Commands;
using BlackListApi.Data;
using Microsoft.EntityFrameworkCore;


namespace Tests;

[TestClass]
public class BlackListEmailHandlerTests
{
    private EmailsDbContext _dbContext;
    private BlackListEmailHandler _handler;

    [TestInitialize]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<EmailsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new EmailsDbContext(options);
        _handler = new BlackListEmailHandler(_dbContext);
    }

    [TestMethod]
    public async Task Handle_ShouldAddEmailToBlackList()
    {
        var command = new BlackListEmailCommand
        {
            Email = "test@example.com",
            BlockedReason = "Spam",
            AppUuid = Guid.NewGuid().ToString(),
            SourceIp = "127.0.0.1"
        };

        var response = await _handler.Handle(command, CancellationToken.None);


        Assert.AreEqual("Email added to the blacklist", response.Message);
        var blackListedEmail = await _dbContext.BlackList.FirstOrDefaultAsync(b => b.Email == command.Email);
        Assert.IsNotNull(blackListedEmail);
        Assert.AreEqual(command.Email, blackListedEmail.Email);
    }
}

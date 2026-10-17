
using System.Data;

namespace Orvanta.ConversationService.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

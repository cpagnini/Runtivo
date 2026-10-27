using Microsoft.EntityFrameworkCore;

namespace Runtivo.Api.Data;

public class RuntivoDbContext(DbContextOptions<RuntivoDbContext> options) : DbContext(options)
{
}

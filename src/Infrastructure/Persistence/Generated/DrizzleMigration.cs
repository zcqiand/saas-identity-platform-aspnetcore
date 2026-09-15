using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class DrizzleMigration
{
    public int Id { get; set; }

    public string Hash { get; set; } = null!;

    public long? CreatedAt { get; set; }
}

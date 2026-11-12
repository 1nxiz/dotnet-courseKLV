using Microsoft.Extensions.Logging.Abstractions;
using AutoMapper;
using Loans.Bll.Mapping;

namespace Loans.Tests;

public class MappingConfigurationTests
{
    [Fact]
    public void AutoMapper_configuration_is_valid()
    {
        var configuration = new MapperConfiguration(cfg => cfg.AddMaps(typeof(LoanProfile).Assembly), NullLoggerFactory.Instance);
        configuration.AssertConfigurationIsValid();
    }
}

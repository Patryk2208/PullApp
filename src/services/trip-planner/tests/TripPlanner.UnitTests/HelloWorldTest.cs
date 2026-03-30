namespace TripPlanner.UnitTests;

public class HelloWorldTest
{
    [Fact]
    public void HelloTest_ShouldAccept()
    {
        // Arrange
        var hw = new HelloWorld();
        
        //Act
        var msg = hw.Hello();
        
        //Assert
        Assert.Equal("Hello World!", msg);
    }
    
    [Fact]
    public void HelloTest_ShouldFail()
    {
        // Arrange
        var hw = new HelloWorld();
        
        //Act
        var msg = hw.Hello();
        
        //Assert
        Assert.NotEqual("Bye, World!", msg);
    }
}
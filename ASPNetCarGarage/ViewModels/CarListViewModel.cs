using CarInventorySystem.Models;

namespace CarInventorySystem.ViewModels;

public class CarListViewModel
{
    public IEnumerable<Car> Cars { get; set; }
}
using ASPNetCarGarage.Data;
using ASPNetCarGarage.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;

namespace ASPNetCarGarage.Controllers;

public class CarInventoryController(CarInventoryDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var cars = await db.Cars
            .OrderBy(c => c.Id)
            .AsNoTracking()
            .ToListAsync();
        
        ViewData["Title"] = "Home";
        return View(cars);
    }

    public IActionResult Add()
    {
        ViewData["Title"] = "Add new car";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Add(Car car)
    {
        if (!ModelState.IsValid)
        {
            return View(car);
        }

        await db.Cars.AddAsync(car);
        await db.SaveChangesAsync();
        return RedirectToAction("Index");
    }

    public async Task<IActionResult> Edit(ObjectId id)
    {
        var car = await db.Cars.FirstOrDefaultAsync(c => c.Id == id);
        if (car == null)
        {
            return NotFound();
        }
        
        ViewData["Title"] = "Edit " + car.NumberPlate;
        return View(car);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Car car)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }
        
        var carToUpdate = await db.Cars.FirstOrDefaultAsync(c => c.Id == car.Id);
        if (carToUpdate == null)
        {
            return NotFound();
        }

        carToUpdate.Model = car.Model;
        carToUpdate.NumberPlate = car.NumberPlate;
        carToUpdate.Location = car.Location;
        carToUpdate.IsBooked = car.IsBooked;

        await db.SaveChangesAsync();
        return View(car);
    }

    public async Task<IActionResult> Delete(ObjectId id)
    {
        var car = await db.Cars.FirstOrDefaultAsync(c => c.Id == id);
        if (car == null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Delete " + car.NumberPlate;
        return View(car);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Car car)
    {
        try
        {
            var foundCar = db.Cars.FirstOrDefault(c => c.Id == car.Id);
            if (foundCar == null)
            {
                return NotFound();
            }
            
            db.Cars.Remove(foundCar);
            await db.SaveChangesAsync();
            TempData["CarDeleted"] = "Car deleted successfully!";

            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ViewData["ErrorMessage"] = $"Deleting the car failed, please try again! Error: {ex.Message}";
            return View(car);
        }
    }
}
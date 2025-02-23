using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Globalization;
using Wafi.SampleTest.Dtos;
using Wafi.SampleTest.Entities;
using Wafi.SampleTest.Mapper;

namespace Wafi.SampleTest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingsController : ControllerBase
    {
        private readonly WafiDbContext _context;

        public BookingsController(WafiDbContext context)
        {
            _context = context;
        }

        // GET: api/Bookings
        [HttpGet("GetBookings")]
        public async Task<ActionResult<IEnumerable<BookingCalendarDto>>> GetCalendarBookings([FromQuery] BookingFilterDto input)
        {
            // TO DO: convert the database bookings to calendar view (date, start time, end time). Consiser NoRepeat, Daily and Weekly options
            // Get booking from the database and filter the data
            var bookingsQuery =  _context.Bookings.AsQueryable();
            
            bookingsQuery = bookingsQuery.Include(e => e.Car).Where(e => e.CarId == input.CarId);

            var bookingCalendarDTOs = new List<BookingCalendarDto>();
            foreach(Booking booking in bookingsQuery)
            {
                var bookedDates = extractAllDatesInABooking(booking);
                if (bookedDates.First() >= input.StartBookingDate 
                    && bookedDates.Last() <= input.EndBookingDate)
                {
                    bookingCalendarDTOs.Add(booking.ToBookingCalendarDTOFromBooking());
                }
            }
            return Ok(bookingCalendarDTOs);

        


        }

        // POST: api/Bookings
        [HttpPost("CreateBooking")]
        public async Task<ActionResult<CreateUpdateBookingDto>> PostBooking(CreateUpdateBookingDto booking) 
        {
            // TO DO: Validate if any booking time conflicts with existing data. Return error if any conflicts
            if (booking.CarId == Guid.Empty)
            {
                return BadRequest("CarId cannot be empty.");
            }
            var requestedCar = await _context.Cars.FindAsync(booking.CarId);
            if (requestedCar == null)
            {
                return BadRequest($"No car of id: {booking.CarId}");
            }
            var today = DateOnly.FromDateTime(DateTime.Now);
            var currentTime = DateTime.Now.TimeOfDay;
            if (booking.BookingDate < today
                || (booking.BookingDate == today && booking.StartTime < currentTime))
            {
                return BadRequest("Can't book past dates");
            }
            if (booking.StartTime >= booking.EndTime)
            {
                return BadRequest("Starttime can't be same as or after endtime");
            }

            if (booking.RepeatOption != RepeatOption.DoesNotRepeat)
            {
                if (booking.EndRepeatDate == null)
                {
                    return BadRequest("Ending date of repeated schedule required");
                }
                if (booking.EndRepeatDate <= booking.BookingDate)
                {
                    return BadRequest("Ending date can not be before or equal to booking date");
                }
            }
            var bookingsOnThisCar = await _context.Bookings.Where(e => e.CarId == booking.CarId).ToListAsync();
            (DateOnly, TimeSpan, TimeSpan, bool) conflicStatus = isBookingTimeConflicting(bookingsOnThisCar, booking.ToBookingFromCreateUpdateBookingDTO());
            if (!bookingsOnThisCar.IsNullOrEmpty()
                    && conflicStatus.Item4)
                {
                    return BadRequest($"Time Conflict! The car is booked on {conflicStatus.Item1} from {conflicStatus.Item2} to {conflicStatus.Item3}");
                }



            await _context.Bookings.AddAsync(booking.ToBookingFromCreateUpdateBookingDTO());
            await _context.SaveChangesAsync();

            return Ok(booking);

        }

        // GET: api/SeedData
        // For test purpose
        [HttpGet("SeedData")]
        public async Task<IEnumerable<BookingCalendarDto>> GetSeedData()
        {
            var cars = await _context.Cars.ToListAsync();

            if (!cars.Any())
            {
                cars = GetCars().ToList();
                await _context.Cars.AddRangeAsync(cars);
                await _context.SaveChangesAsync();
            }

            var bookings = await _context.Bookings.ToListAsync();

            if(!bookings.Any())
            {
                bookings = GetBookings().ToList();

                await _context.Bookings.AddRangeAsync(bookings);
                await _context.SaveChangesAsync();
            }

            var calendar = new Dictionary<DateOnly, List<Booking>>();

            foreach (var booking in bookings)
            {
                var currentDate = booking.BookingDate;
                while (currentDate <= (booking.EndRepeatDate ?? booking.BookingDate))
                {
                    if (!calendar.ContainsKey(currentDate))
                        calendar[currentDate] = new List<Booking>();

                    calendar[currentDate].Add(booking);

                    currentDate = booking.RepeatOption switch
                    {
                        RepeatOption.Daily => currentDate.AddDays(1),
                        RepeatOption.Weekly => currentDate.AddDays(7),
                        _ => booking.EndRepeatDate.HasValue ? booking.EndRepeatDate.Value.AddDays(1) : currentDate.AddDays(1)
                    };
                }
            }

            List<BookingCalendarDto> result = new List<BookingCalendarDto>();

            foreach (var item in calendar)
            {
                foreach(var booking in item.Value)
                {
                    result.Add(new BookingCalendarDto { BookingDate = booking.BookingDate, CarModel = booking.Car.Model, StartTime = booking.StartTime, EndTime = booking.EndTime });
                }
            }

            return result;
        }
        private (DateOnly, TimeSpan, TimeSpan, bool) isBookingTimeConflicting(IList<Booking> scheduledBookings, Booking requestedBooking)
        {
            
            foreach (var booking in scheduledBookings)
            {
                if (!hasTimeOverlap(booking,requestedBooking)) continue;
                if ( (booking.RepeatOption != RepeatOption.DoesNotRepeat) && (requestedBooking.RepeatOption != RepeatOption.DoesNotRepeat))
                {
                    if (!hasDaysOfWeekOverlap(booking, requestedBooking)) continue;
                    if (!hasDateOverlap(booking, requestedBooking)) continue;
                }
                var scheduledBookingDates = extractAllDatesInABooking(booking).ToHashSet();
                var requestedBookingDates = extractAllDatesInABooking(requestedBooking);

                foreach(var requestedBookingDate in requestedBookingDates)
                {
                    if (scheduledBookingDates.Contains(requestedBookingDate)) return (requestedBookingDate, booking.StartTime, booking.EndTime, true);
                }
            }
            return (new DateOnly(), new TimeSpan(), new TimeSpan(), false);
        }

        private IEnumerable<DateOnly> extractAllDatesInABooking(Booking booking)
        {

            var dates = new List<DateOnly>();
            if (booking.RepeatOption == RepeatOption.DoesNotRepeat)
            {
                dates.Add(booking.BookingDate);
            }
            if (booking.RepeatOption == RepeatOption.Daily
                || ( (booking.RepeatOption == RepeatOption.Weekly) && (booking.DaysToRepeatOn == (DaysOfWeek.Sunday | DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Saturday))))
            {
                for(var currentDate = booking.BookingDate; currentDate <= booking.EndRepeatDate; currentDate = currentDate.AddDays(1))
                {
                    dates.Add(currentDate);
                }
            }
            if (booking.RepeatOption == RepeatOption.Weekly)
            {
                var currentDate = booking.BookingDate;
                dates.Add(currentDate);
                while (currentDate <= booking.EndRepeatDate)
                {
                    foreach (DaysOfWeek day in Enum.GetValues(typeof(DaysOfWeek)))
                    {
                        //if ((booking.DaysToRepeatOn & day) == day)
                        if (((int)booking.DaysToRepeatOn & (int)day) != 0)
                        {
                            var nextDay = currentDate.AddDays(( (int) Math.Log2((double)day) - (int)currentDate.DayOfWeek + 7) % 7);
                            if (nextDay <= booking.EndRepeatDate) dates.Add(nextDay);
                        }
                    }
                    currentDate = currentDate.AddDays(7); 
                }
            }
            return dates;
        }

        private bool hasTimeOverlap(Booking scheduledBooking, Booking requestedBooking)
        {
            if( (requestedBooking.StartTime >= scheduledBooking.StartTime && requestedBooking.StartTime <= scheduledBooking.EndTime)
                || (requestedBooking.EndTime >= scheduledBooking.StartTime && requestedBooking.EndTime <= scheduledBooking.EndTime)
                || (requestedBooking.StartTime <= scheduledBooking.StartTime && requestedBooking.EndTime >= scheduledBooking.EndTime)) 
            {
                return true;
            }
            return false;
        }

        private bool hasDaysOfWeekOverlap(Booking scheduledBooking, Booking requestedBooking)
        {
            if(scheduledBooking.BookingDate == requestedBooking.BookingDate
                || scheduledBooking.EndRepeatDate == requestedBooking.EndRepeatDate
                || scheduledBooking.BookingDate == requestedBooking.EndRepeatDate
                || scheduledBooking.EndRepeatDate == requestedBooking.BookingDate
                || ((int)scheduledBooking.DaysToRepeatOn & (int)requestedBooking.DaysToRepeatOn) != 0)
            {
                return true;
            }
            return false;
        }

        private bool hasDateOverlap(Booking scheduledBooking, Booking requestedBooking)
        {
            if ((requestedBooking.BookingDate >= scheduledBooking.BookingDate && requestedBooking.BookingDate <= scheduledBooking.EndRepeatDate)
                || (requestedBooking.EndRepeatDate >= scheduledBooking.BookingDate && requestedBooking.EndRepeatDate <= scheduledBooking.EndRepeatDate)
                || (requestedBooking.BookingDate <= scheduledBooking.BookingDate && requestedBooking.EndRepeatDate >= scheduledBooking.EndRepeatDate))
            {
                return true;
            }
            return false;
        }

        #region Sample Data

        private IList<Car> GetCars()
        {
            var cars = new List<Car>
            {
                new Car { Id = Guid.NewGuid(), Make = "Toyota", Model = "Corolla" },
                new Car { Id = Guid.NewGuid(), Make = "Honda", Model = "Civic" },
                new Car { Id = Guid.NewGuid(), Make = "Ford", Model = "Focus" }
            };

            return cars;
        }

        private IList<Booking> GetBookings()
        {
            var cars = GetCars();

            var bookings = new List<Booking>
            {
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 5), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(12, 0, 0), RepeatOption = RepeatOption.DoesNotRepeat, RequestedOn = DateTime.Now, CarId = cars[0].Id, Car = cars[0] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 10), StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(16, 0, 0), RepeatOption = RepeatOption.Daily, EndRepeatDate = new DateOnly(2025, 2, 20), RequestedOn = DateTime.Now, CarId = cars[1].Id, Car = cars[1] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 15), StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 30, 0), RepeatOption = RepeatOption.Weekly, EndRepeatDate = new DateOnly(2025, 3, 31), RequestedOn = DateTime.Now, DaysToRepeatOn = DaysOfWeek.Monday, CarId = cars[2].Id,  Car = cars[2] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 1), StartTime = new TimeSpan(11, 0, 0), EndTime = new TimeSpan(13, 0, 0), RepeatOption = RepeatOption.DoesNotRepeat, RequestedOn = DateTime.Now, CarId = cars[0].Id, Car = cars[0] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 7), StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(10, 0, 0), RepeatOption = RepeatOption.Weekly, EndRepeatDate = new DateOnly(2025, 3, 28), RequestedOn = DateTime.Now, DaysToRepeatOn = DaysOfWeek.Friday, CarId = cars[1].Id, Car = cars[1] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 15), StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(17, 0, 0), RepeatOption = RepeatOption.Daily, EndRepeatDate = new DateOnly(2025, 3, 20), RequestedOn = DateTime.Now, CarId = cars[2].Id,  Car = cars[2] }
            };

            return bookings;
        }


            #endregion

        }
}

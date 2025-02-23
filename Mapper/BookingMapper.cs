using System.ComponentModel.DataAnnotations;
using Wafi.SampleTest.Dtos;
using Wafi.SampleTest.Entities;

namespace Wafi.SampleTest.Mapper
{
    public static class BookingMapper
    {

        public static BookingCalendarDto ToBookingCalendarDTOFromBooking(this Booking booking)
        {
            return new BookingCalendarDto
            {
                BookingDate = booking.BookingDate,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                CarModel = booking.Car.Model
            };
        }

        public static Booking ToBookingFromCreateUpdateBookingDTO(this CreateUpdateBookingDto bookingDTO)
        {
            return new Booking
            {
                BookingDate = bookingDTO.BookingDate,
                StartTime = bookingDTO.StartTime,
                EndTime = bookingDTO.EndTime,
                RepeatOption = bookingDTO.RepeatOption,
                EndRepeatDate = bookingDTO.EndRepeatDate,
                DaysToRepeatOn = bookingDTO.DaysToRepeatOn,
                RequestedOn = bookingDTO.RequestedOn,
                CarId = bookingDTO.CarId
            };
        }


    }
}

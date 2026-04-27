namespace ClinicAdoNetApi.DTO;

public class CreateAppointmentRequestDto
{
    public int IdPatient {get; set;}
    public int IdDoctor {get; set;}
    public DateTime AppointmentDate {get; set;}
    public string reason { get; set; } = string.Empty;
}
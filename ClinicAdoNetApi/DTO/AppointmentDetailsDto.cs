namespace ClinicAdoNetApi.DTO;

public class AppointmentDetailsDto
{
    public int IdAppointment {get; set;}
    public DateTime AppointmentDate {get; set;}
    public string Reason {get; set;} = string.Empty;
    public string PatientFullName {get; set;} = string.Empty;
    public string PatientEmail {get; set;} = string.Empty;
    public string PatientPhoneNumber {get; set;} = string.Empty;
    public string Status {get; set;} = string.Empty;
    public string DoctorLicenseNumber {get; set;} = string.Empty;
    public string? InternalNotes {get; set;} 
    public DateTime CreatedAt {get; set;}
}
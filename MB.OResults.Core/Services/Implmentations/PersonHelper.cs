namespace MB.OResults.Core;

public static class PersonHelper {
  public static string GetPersonId(this PersonStart p, string grade) {
    return GetPersonId(p.Person, grade, p.Organisation);
  }

  public static string GetPersonId(this PersonEntry p, string grade) {
    return GetPersonId(p.Person, grade, p.Organisation);
  }

  public static string GetPersonId(this PersonResult p, string grade) {
    return GetPersonId(p.Person, grade, p.Organisation);
  }

  public static string GetPersonId(this Person person, string grade, Organisation organisation) {
    return CalculateMD5Hash($"{grade} {person?.Name?.Family} {person?.Name?.Given} {organisation?.Name}");
  }

  public static string CalculateMD5Hash(string input) {
    return String.Join("", MD5.HashData(Encoding.ASCII.GetBytes(input)).Select(p => p.ToString("X2")));
  }

  public static string GetStatusCode(string status) {
    if (string.IsNullOrWhiteSpace(status)) {
      return "";
    }

    if (status == ResultStatus.Active.ToString()) {
      return "";
    }

    if (status == ResultStatus.Inactive.ToString()) {
      return "";
    }

    if (status == ResultStatus.OK.ToString()) {
      return "OK";
    }

    if (status == ResultStatus.MissingPunch.ToString()) {
      return "MP";
    }

    if (status == ResultStatus.DidNotFinish.ToString()) {
      return "DNF";
    }

    if (status == ResultStatus.DidNotStart.ToString()) {
      return "DNS";
    }

    if (status == ResultStatus.Disqualified.ToString()) {
      return "DSQ";
    }

    if (status == ResultStatus.OverTime.ToString()) {
      return "OT";
    }

    return string.Concat(status.Select(x => Char.IsUpper(x) ? " " + x : x.ToString())).TrimStart(' ');
  }
}
/**
 * Converts JavaScript's Sunday-first weekday to the database convention:
 * Monday = 0 through Sunday = 6.
 */
export function jsDayToDbWeekday(date: Date): number {
  return (date.getDay() + 6) % 7;
}

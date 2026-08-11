export type WallClockParts = { year: number; month: number; day: number; hour: number; minute: number; second: number };

function partsAt(instant: Date, timeZone: string): WallClockParts {
  const values = Object.fromEntries(new Intl.DateTimeFormat('en-GB', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).formatToParts(instant).filter((part) => part.type !== 'literal').map((part) => [part.type, Number(part.value)]));
  return { year: values.year, month: values.month, day: values.day, hour: values.hour, minute: values.minute, second: values.second };
}

export function toInstituteWallClock(instant: Date, timeZone?: string): Date {
  if (!timeZone) return new Date(instant);
  const p = partsAt(instant, timeZone);
  return new Date(p.year, p.month - 1, p.day, p.hour, p.minute, p.second, instant.getMilliseconds());
}

export function wallClockToInstant(wall: Date, timeZone?: string): Date {
  if (!timeZone) return new Date(wall);
  const wanted = Date.UTC(wall.getFullYear(), wall.getMonth(), wall.getDate(), wall.getHours(), wall.getMinutes(), wall.getSeconds(), wall.getMilliseconds());
  let guess = wanted;
  for (let index = 0; index < 2; index += 1) {
    const shown = partsAt(new Date(guess), timeZone);
    const represented = Date.UTC(shown.year, shown.month - 1, shown.day, shown.hour, shown.minute, shown.second, wall.getMilliseconds());
    guess += wanted - represented;
  }
  return new Date(guess);
}

export function deviceZoneDiffers(timeZone?: string): boolean {
  return Boolean(timeZone && Intl.DateTimeFormat().resolvedOptions().timeZone !== timeZone);
}

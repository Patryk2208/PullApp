const MINIMUM_AGE = 18;

// TODO USE THIS
export function isUserOldEnough(birthDate: Date, currentDate: Date = new Date()): boolean {
    let age = currentDate.getFullYear() - birthDate.getFullYear();
    const monthDifference = currentDate.getMonth() - birthDate.getMonth();

    if (monthDifference < 0 || (monthDifference === 0 && currentDate.getDate() < birthDate.getDate())) {
        age--;
    }

    return age >= MINIMUM_AGE;
}
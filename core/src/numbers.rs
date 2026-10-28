/// Match .NET `Math.Round` midpoint-to-even at the requested decimal places.
pub fn round_digits(value: f64, digits: i32) -> f64 {
    if !value.is_finite() {
        return value;
    }
    let factor = 10f64.powi(digits);
    let scaled = value * factor;
    let floor = scaled.floor();
    let diff = scaled - floor;
    let rounded = if (diff - 0.5).abs() < 1e-9 {
        if (floor as i64).rem_euclid(2) == 0 { floor } else { floor + 1.0 }
    } else if diff > 0.5 {
        floor + 1.0
    } else {
        floor
    };
    rounded / factor
}

pub fn round1(value: f64) -> f64 {
    round_digits(value, 1)
}

pub fn round2(value: f64) -> f64 {
    round_digits(value, 2)
}

pub fn clamp_percent(value: f64) -> f64 {
    value.clamp(0.0, 100.0)
}

namespace Metering.Types

type IncludedQuantitySpecification = 
    { /// Monthly quantity included in base.
      /// Quantity of dimension included per month for customers paying the recurring monthly fee, must be an integer. It can be 0 or unlimited.
      Monthly: Quantity option

      /// Annual quantity included in base.
      /// Quantity of dimension included per each year for customers paying the recurring annual fee, must be an integer. Can be 0 or unlimited.
      Annually: Quantity option }

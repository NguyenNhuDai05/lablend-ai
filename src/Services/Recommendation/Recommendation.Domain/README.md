# Recommendation Domain

`RecommendationSession` owns Student/purpose/constraints/model/UTC timestamp and ordered `RecommendationItem` references. Invariants enforce bounded text, valid IDs, unique equipment references and at most six ranked suggestions. Catalog records are not duplicated and no service database is referenced. The domain has no EF/provider dependencies.
